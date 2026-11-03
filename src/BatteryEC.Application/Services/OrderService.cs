using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Repositories;

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
                PaymentStatus = "Unpaid",
                PlacedAt = DateTime.UtcNow
            };

            decimal subtotal = 0;
            foreach (var item in dto.Items)
            {
                var variant = await _variantRepository.GetByIdAsync(item.ProductVariantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Product variant #{item.ProductVariantId} not found.");

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

                // Reserve inventory for online order
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory != null)
                {
                    inventory.QuantityReserved += item.Quantity;
                    inventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(inventory);
                }
            }

            order.Subtotal = subtotal;
            order.TotalAmount = subtotal; // Before vouchers / shipping

            await _orderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return MapToDto(order);
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
        if (order == null || order.OrderStatus != "PendingConfirmation")
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

    public async Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
            if (order == null || order.OrderStatus == "Cancelled" || order.OrderStatus == "Completed")
            {
                return false;
            }

            order.OrderStatus = "Cancelled";
            _orderRepository.Update(order);

            // Release reserved inventory
            foreach (var item in order.Items)
            {
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory != null)
                {
                    inventory.QuantityReserved = Math.Max(0, inventory.QuantityReserved - item.Quantity);
                    inventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(inventory);
                }
            }

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
