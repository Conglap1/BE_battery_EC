using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Application.Services;

public class PosService : IPosService
{
    private readonly ISaleRepository _saleRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGenericRepository<ProductVariant> _variantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PosService(
        ISaleRepository saleRepository,
        IInventoryRepository inventoryRepository,
        IGenericRepository<ProductVariant> variantRepository,
        IUnitOfWork unitOfWork)
    {
        _saleRepository = saleRepository;
        _inventoryRepository = inventoryRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SaleDto> CreateSaleAsync(CreateSaleDto dto, long cashierUserId, CancellationToken cancellationToken = default)
    {
        // 1. VALIDATION ĐẦU VÀO & GIỎ HÀNG (Khắc phục ISSUE-32)
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("Giỏ hàng bán lẻ tại quầy không được để trống.");
        }
        if (dto.DiscountAmount < 0)
        {
            throw new InvalidOperationException("Tiền chiết khấu không được âm.");
        }

        foreach (var item in dto.Items)
        {
            if (item.ProductVariantId <= 0)
            {
                throw new InvalidOperationException("Mã sản phẩm không hợp lệ.");
            }
            if (item.Quantity <= 0)
            {
                throw new InvalidOperationException($"Số lượng bán cho sản phẩm #{item.ProductVariantId} phải lớn hơn 0.");
            }
        }

        var paymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "Cash" : dto.PaymentMethod.Trim();
        if (paymentMethod != "Cash" && paymentMethod != "Card" && paymentMethod != "BankTransfer")
        {
            throw new InvalidOperationException($"Phương thức thanh toán '{dto.PaymentMethod}' không hợp lệ. Các phương thức hỗ trợ: Cash, Card, BankTransfer.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // 2. KIỂM TRA TỒN KHO KHẢ DỤNG CHO TOÀN BỘ GIỎ HÀNG TRƯỚC KHI TRỪ (Khắc phục ISSUE-05)
            var inventoryMap = new Dictionary<long, BatteryEC.Domain.Entities.Inventory.Inventory>();
            foreach (var group in dto.Items.GroupBy(i => i.ProductVariantId))
            {
                var variantId = group.Key;
                var totalNeeded = group.Sum(i => i.Quantity);

                var inventory = await _inventoryRepository.GetByVariantIdAsync(variantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Không tìm thấy bản ghi tồn kho cho sản phẩm #{variantId}.");

                if (inventory.AvailableStock < totalNeeded)
                {
                    throw new InvalidOperationException($"Sản phẩm #{variantId} không đủ tồn kho khả dụng để bán tại quầy (Khả dụng: {inventory.AvailableStock}, Cần bán: {totalNeeded}).");
                }

                inventoryMap[variantId] = inventory;
            }

            // 3. KHỞI TẠO HÓA ĐƠN VÀ CHI TIẾT SẢN PHẨM BÁN
            var sale = new Sale
            {
                SaleCode = $"POS-{DateTime.UtcNow:yyyyMMddHHmmss}-{cashierUserId}",
                SoldByUserId = cashierUserId,
                CustomerId = dto.CustomerId,
                CustomerName = dto.CustomerName,
                CustomerPhone = dto.CustomerPhone,
                PaymentMethod = paymentMethod,
                SaleStatus = "Completed",
                Note = dto.Note,
                SoldAt = DateTime.UtcNow
            };

            decimal subtotal = 0;
            foreach (var item in dto.Items)
            {
                var variant = await _variantRepository.GetByIdAsync(item.ProductVariantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Product variant #{item.ProductVariantId} not found.");

                decimal lineTotal = item.Quantity * variant.SellingPrice;
                subtotal += lineTotal;

                sale.Items.Add(new SaleItem
                {
                    ProductVariantId = item.ProductVariantId,
                    ProductName = variant.Product?.Name ?? variant.VariantName,
                    VariantName = variant.VariantName,
                    SKU = variant.SKU,
                    UnitPrice = variant.SellingPrice,
                    CostPrice = variant.CostPrice ?? 0,
                    WarrantyMonths = variant.WarrantyMonths,
                    Quantity = item.Quantity,
                    LineTotal = lineTotal
                });
            }

            // Kiểm tra chiết khấu không vượt quá tổng tiền hàng (Khắc phục ISSUE-32)
            if (dto.DiscountAmount > subtotal)
            {
                throw new InvalidOperationException($"Tiền chiết khấu ({dto.DiscountAmount:N0} đ) không được vượt quá tổng tiền hàng ({subtotal:N0} đ).");
            }

            sale.SubtotalAmount = subtotal;
            sale.DiscountAmount = dto.DiscountAmount;
            sale.TotalAmount = subtotal - dto.DiscountAmount;

            // 4. LƯU SALE TRƯỚC ĐỂ DB SINH RA SaleId (Khắc phục ISSUE-06)
            await _saleRepository.AddAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 5. TRỪ KHO VÀ GHI THẺ KHO CounterSale VỚI ReferenceId = sale.SaleId CHÍNH XÁC
            foreach (var item in sale.Items)
            {
                var inventory = inventoryMap[item.ProductVariantId];
                inventory.QuantityOnHand -= item.Quantity;
                inventory.Version++;
                inventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(inventory);

                await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
                {
                    ProductVariantId = item.ProductVariantId,
                    TransactionType = InventoryTransactionType.CounterSale,
                    QuantityChange = -item.Quantity,
                    ReferenceType = "Sale",
                    ReferenceId = sale.SaleId, // ID thực tế vừa được sinh
                    UnitCost = item.CostPrice,
                    CreatedByUserId = cashierUserId,
                    Note = $"Sold at counter {sale.SaleCode}",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return MapToDto(sale);
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

    public async Task<SaleDto?> GetSaleByCodeAsync(string saleCode, CancellationToken cancellationToken = default)
    {
        var sale = await _saleRepository.GetByCodeAsync(saleCode, cancellationToken);
        if (sale == null) return null;

        return MapToDto(sale);
    }

    private static SaleDto MapToDto(Sale sale)
    {
        return new SaleDto
        {
            SaleId = sale.SaleId,
            SaleCode = sale.SaleCode,
            CreatedByUserId = sale.SoldByUserId,
            CustomerId = sale.CustomerId,
            CustomerName = sale.CustomerName,
            CustomerPhone = sale.CustomerPhone,
            Subtotal = sale.SubtotalAmount,
            DiscountAmount = sale.DiscountAmount,
            TotalAmount = sale.TotalAmount,
            PaymentMethod = sale.PaymentMethod,
            SaleStatus = sale.SaleStatus,
            SoldAt = sale.SoldAt,
            Items = sale.Items.Select(i => new SaleItemDto
            {
                SaleItemId = i.SaleItemId,
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
