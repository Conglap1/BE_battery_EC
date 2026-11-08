using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Repositories;

namespace BatteryEC.Application.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGenericRepository<ProductVariant> _variantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public InventoryService(
        IInventoryRepository inventoryRepository,
        IGenericRepository<ProductVariant> variantRepository,
        IUnitOfWork unitOfWork)
    {
        _inventoryRepository = inventoryRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<InventoryDto?> GetStockAsync(long variantId, CancellationToken cancellationToken = default)
    {
        var inventory = await _inventoryRepository.GetByVariantIdAsync(variantId, cancellationToken);
        if (inventory == null) return null;

        return new InventoryDto
        {
            ProductVariantId = inventory.ProductVariantId,
            SKU = inventory.ProductVariant?.SKU ?? string.Empty,
            VariantName = inventory.ProductVariant?.VariantName ?? string.Empty,
            QuantityOnHand = inventory.QuantityOnHand,
            QuantityReserved = inventory.QuantityReserved,
            ReorderLevel = inventory.ReorderLevel,
            UpdatedAt = inventory.UpdatedAt
        };
    }

    public async Task<bool> AdjustStockAsync(StockAdjustmentDto dto, long userId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (dto == null || dto.ProductVariantId <= 0 || dto.QuantityChange == 0)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            var variant = await _variantRepository.GetByIdAsync(dto.ProductVariantId, cancellationToken);
            if (variant == null)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            var inventory = await _inventoryRepository.GetByVariantIdAsync(dto.ProductVariantId, cancellationToken);
            if (inventory == null)
            {
                if (dto.QuantityChange < 0)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return false;
                }

                inventory = new BatteryEC.Domain.Entities.Inventory.Inventory
                {
                    ProductVariantId = dto.ProductVariantId,
                    QuantityOnHand = dto.QuantityChange,
                    QuantityReserved = 0,
                    ReorderLevel = 0,
                    Version = 1,
                    UpdatedAt = DateTime.UtcNow
                };
                await _inventoryRepository.AddAsync(inventory, cancellationToken);
            }
            else
            {
                if (inventory.QuantityOnHand + dto.QuantityChange < inventory.QuantityReserved)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return false; // Cannot reduce stock below reserved stock
                }

                inventory.QuantityOnHand += dto.QuantityChange;
                inventory.Version++;
                inventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(inventory);
            }

            await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
            {
                ProductVariantId = dto.ProductVariantId,
                TransactionType = "StockAudit",
                QuantityChange = dto.QuantityChange,
                ReferenceType = "ManualAdjustment",
                ReferenceId = 0,
                UnitCost = dto.UnitCost ?? 0,
                CreatedByUserId = userId,
                Note = dto.Reason,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> ReserveStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return false;
        }

        var inventory = await _inventoryRepository.GetByVariantIdAsync(variantId, cancellationToken);
        if (inventory == null || inventory.AvailableStock < quantity)
        {
            return false;
        }

        inventory.QuantityReserved += quantity;
        inventory.Version++;
        inventory.UpdatedAt = DateTime.UtcNow;
        _inventoryRepository.Update(inventory);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return false;
        }

        var inventory = await _inventoryRepository.GetByVariantIdAsync(variantId, cancellationToken);
        if (inventory == null) return false;

        inventory.QuantityReserved = Math.Max(0, inventory.QuantityReserved - quantity);
        inventory.Version++;
        inventory.UpdatedAt = DateTime.UtcNow;
        _inventoryRepository.Update(inventory);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
