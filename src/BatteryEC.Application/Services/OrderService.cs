using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Application.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGenericRepository<ProductVariant> _variantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(
        IOrderRepository orderRepository,
        IInventoryRepository inventoryRepository,
        IGenericRepository<ProductVariant> variantRepository,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _inventoryRepository = inventoryRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        if (dto.Items == null || !dto.Items.Any())
        {
            throw new InvalidOperationException("Order must contain at least one item.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = new Order
            {
                OrderCode = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{dto.UserId}",
                UserId = dto.UserId,
                RecipientName = dto.RecipientName,
                RecipientPhone = dto.RecipientPhone,
                ShippingAddress = dto.ShippingAddress,
                PaymentMethod = dto.PaymentMethod,
                CustomerNote = dto.CustomerNote,
                OrderStatus = "PendingConfirmation",
                PaymentStatus = "Pending",
                PlacedAt = DateTime.UtcNow
            };

            decimal subtotal = 0;
            foreach (var item in dto.Items)
            {
                if (item.Quantity <= 0)
                {
                    throw new InvalidOperationException($"Invalid quantity {item.Quantity} for variant #{item.ProductVariantId}. Quantity must be positive.");
                }

                var variant = await _variantRepository.GetByIdAsync(item.ProductVariantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Product variant #{item.ProductVariantId} not found.");

                // Concurrency and stock check P0.6: Validate available stock before reservation
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory == null || inventory.AvailableStock < item.Quantity)
                {
                    var available = inventory?.AvailableStock ?? 0;
                    throw new InvalidOperationException($"Insufficient available stock for variant #{item.ProductVariantId}. Requested: {item.Quantity}, Available: {available}.");
                }

                decimal lineTotal = item.Quantity * variant.SellingPrice;
                subtotal += lineTotal;

                order.Items.Add(new OrderItem
                {
                    ProductVariantId = item.ProductVariantId,
                    ProductName = variant.Product?.Name ?? variant.VariantName,
                    VariantName = variant.VariantName,
                    SKU = variant.SKU,
                    UnitPrice = variant.SellingPrice,
                    CostPrice = variant.CostPrice,
                    WarrantyMonths = variant.WarrantyMonths,
                    Quantity = item.Quantity,
                    LineTotal = lineTotal
                });

                // Reserve inventory for online order and bump Version for optimistic concurrency
                inventory.QuantityReserved += item.Quantity;
                inventory.Version++;
                inventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(inventory);
            }

            order.Subtotal = subtotal;
            order.TotalAmount = subtotal; // Before vouchers / shipping

            await _orderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return MapToDto(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new InvalidOperationException("Stock conflict detected, please retry.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OrderDto?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByCodeAsync(orderCode, cancellationToken);
        if (order == null) return null;

        return MapToDto(order);
    }

    public async Task<bool> ConfirmOrderAsync(long orderId, long confirmedByUserId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            return false;
        }

        if (order.OrderStatus == "Confirmed")
        {
            return true; // Idempotent success
        }

        if (order.OrderStatus != "PendingConfirmation")
        {
            return false;
        }

        order.OrderStatus = "Confirmed";
        order.ConfirmedByUserId = confirmedByUserId;
        order.ConfirmedAt = DateTime.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ShipOrderAsync(long orderId, string carrierName, string trackingCode, long shippedByUserId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
            if (order == null || (order.OrderStatus != "Confirmed" && order.OrderStatus != "Processing"))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            order.OrderStatus = "Shipped";
            order.Shipments.Add(new Shipment
            {
                OrderId = order.OrderId,
                ShippingMethodId = order.ShippingMethodId ?? 1,
                CarrierName = carrierName,
                TrackingNumber = trackingCode,
                ShipmentStatus = "InTransit",
                ShippedAt = DateTime.UtcNow
            });
            _orderRepository.Update(order);

            // Two-Tier Inventory: Deduct physical inventory & release reserved stock upon 3PL handover
            foreach (var item in order.Items)
            {
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory != null)
                {
                    inventory.QuantityOnHand -= item.Quantity;
                    inventory.QuantityReserved = Math.Max(0, inventory.QuantityReserved - item.Quantity);
                    inventory.Version++;
                    inventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(inventory);
                }

                await _inventoryRepository.AddTransactionAsync(new BatteryEC.Domain.Entities.Inventory.InventoryTransaction
                {
                    ProductVariantId = item.ProductVariantId,
                    TransactionType = "OnlineSale",
                    QuantityChange = -item.Quantity,
                    ReferenceType = "Order",
                    ReferenceId = order.OrderId,
                    UnitCost = item.CostPrice ?? 0,
                    CreatedByUserId = shippedByUserId,
                    Note = $"Handed to {carrierName} tracking {trackingCode}",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new InvalidOperationException("Stock conflict detected, please retry.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> CompleteOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
            if (order == null || order.OrderStatus != "Shipped")
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            order.OrderStatus = "Completed";
            order.PaymentStatus = "Paid";
            order.CompletedAt = DateTime.UtcNow;

            // Activate electronic warranty by updating shipment to Delivered
            var shipment = order.Shipments.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
            if (shipment != null)
            {
                shipment.ShipmentStatus = "Delivered";
                shipment.DeliveredAt = DateTime.UtcNow;
            }
            else
            {
                order.Shipments.Add(new Shipment
                {
                    OrderId = order.OrderId,
                    ShippingMethodId = order.ShippingMethodId ?? 1,
                    CarrierName = "Standard",
                    TrackingNumber = "N/A",
                    ShipmentStatus = "Delivered",
                    ShippedAt = order.PlacedAt,
                    DeliveredAt = DateTime.UtcNow
                });
            }

            _orderRepository.Update(order);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
            // No-Return Policy: Disallow cancellation if already Cancelled, Completed, or Shipped
            if (order == null || order.OrderStatus == "Cancelled" || order.OrderStatus == "Completed" || order.OrderStatus == "Shipped")
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            order.OrderStatus = "Cancelled";
            order.CancelledAt = DateTime.UtcNow;
            _orderRepository.Update(order);

            // Release reserved inventory
            foreach (var item in order.Items)
            {
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory != null)
                {
                    inventory.QuantityReserved = Math.Max(0, inventory.QuantityReserved - item.Quantity);
                    inventory.Version++;
                    inventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(inventory);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new InvalidOperationException("Stock conflict detected, please retry.");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static OrderDto MapToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.OrderId,
            OrderCode = order.OrderCode,
            UserId = order.UserId,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            ShippingAddress = order.ShippingAddress,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            ShippingFee = order.ShippingFee,
            TotalAmount = order.TotalAmount,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            OrderStatus = order.OrderStatus,
            PlacedAt = order.PlacedAt,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            Items = order.Items.Select(i => new OrderItemDto
            {
                OrderItemId = i.OrderItemId,
                ProductVariantId = i.ProductVariantId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                SKU = i.SKU,
                UnitPrice = i.UnitPrice,
                WarrantyMonths = i.WarrantyMonths,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            }).ToList()
        };
    }
}
