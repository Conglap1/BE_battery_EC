using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Pos;
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
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var sale = new Sale
            {
                SaleCode = $"POS-{DateTime.UtcNow:yyyyMMddHHmmss}-{cashierUserId}",
                SoldByUserId = cashierUserId,
                CustomerId = dto.CustomerId,
                CustomerName = dto.CustomerName,
                CustomerPhone = dto.CustomerPhone,
                PaymentMethod = dto.PaymentMethod,
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

                // Immediate stock deduction for retail counter sales
                var inventory = await _inventoryRepository.GetByVariantIdAsync(item.ProductVariantId, cancellationToken);
                if (inventory != null)
                {
                    inventory.QuantityOnHand -= item.Quantity;
                    inventory.Version++;
                    inventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(inventory);
                }

                await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
                {
                    ProductVariantId = item.ProductVariantId,
                    TransactionType = "CounterSale",
                    QuantityChange = -item.Quantity,
                    ReferenceType = "Sale",
                    ReferenceId = 0,
                    UnitCost = variant.CostPrice ?? 0,
                    CreatedByUserId = cashierUserId,
                    Note = $"Sold at counter {sale.SaleCode}",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            sale.SubtotalAmount = subtotal;
            sale.DiscountAmount = dto.DiscountAmount;
            sale.TotalAmount = Math.Max(0, subtotal - dto.DiscountAmount);

            await _saleRepository.AddAsync(sale, cancellationToken);
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
