using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Application.Services;

public class AssemblyService : IAssemblyService
{
    private readonly IAssemblyOrderRepository _assemblyOrderRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IGenericRepository<ProductVariant> _variantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AssemblyService(
        IAssemblyOrderRepository assemblyOrderRepository,
        IInventoryRepository inventoryRepository,
        IGenericRepository<ProductVariant> variantRepository,
        IUnitOfWork unitOfWork)
    {
        _assemblyOrderRepository = assemblyOrderRepository;
        _inventoryRepository = inventoryRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<AssemblyOrderDto?> GetAssemblyOrderByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default)
    {
        var order = await _assemblyOrderRepository.GetByCodeAsync(assemblyCode, cancellationToken);
        if (order == null) return null;

        return MapToDto(order);
    }

    public async Task<AssemblyOrderDto> ExecuteAssemblyOrderAsync(CreateAssemblyOrderDto dto, long userId, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var bundleVariant = await _variantRepository.GetByIdAsync(dto.BundleVariantId, cancellationToken)
                ?? throw new InvalidOperationException($"Bundle variant #{dto.BundleVariantId} not found.");

            decimal totalCost = 0;
            var order = new AssemblyOrder
            {
                AssemblyCode = string.IsNullOrWhiteSpace(dto.AssemblyCode)
                    ? $"ASM-{DateTime.UtcNow:yyyyMMddHHmmss}-{dto.BundleVariantId}"
                    : dto.AssemblyCode,
                BundleVariantId = dto.BundleVariantId,
                QuantityProduced = dto.QuantityProduced,
                Status = "Completed",
                Note = dto.Note,
                CreatedByUserId = userId,
                AssembledAt = DateTime.UtcNow
            };

            foreach (var item in dto.Items)
            {
                var componentVariant = await _variantRepository.GetByIdAsync(item.ComponentVariantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Component variant #{item.ComponentVariantId} not found.");

                decimal lineTotal = item.QuantityConsumed * item.UnitCost;
                totalCost += lineTotal;

                order.Items.Add(new AssemblyOrderItem
                {
                    ComponentVariantId = item.ComponentVariantId,
                    QuantityConsumed = item.QuantityConsumed,
                    UnitCost = item.UnitCost,
                    LineTotal = lineTotal
                });

                // Deduct component stock
                var compInventory = await _inventoryRepository.GetByVariantIdAsync(item.ComponentVariantId, cancellationToken);
                if (compInventory != null)
                {
                    compInventory.QuantityOnHand -= item.QuantityConsumed;
                    compInventory.Version++;
                    compInventory.UpdatedAt = DateTime.UtcNow;
                    _inventoryRepository.Update(compInventory);
                }

                await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
                {
                    ProductVariantId = item.ComponentVariantId,
                    TransactionType = "AssemblyConsumption",
                    QuantityChange = -item.QuantityConsumed,
                    ReferenceType = "AssemblyOrder",
                    ReferenceId = 0,
                    UnitCost = item.UnitCost,
                    CreatedByUserId = userId,
                    Note = $"Consumed in {order.AssemblyCode}",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            order.TotalCost = totalCost;
            order.UnitCost = dto.QuantityProduced > 0 ? Math.Round(totalCost / dto.QuantityProduced, 2) : 0;

            await _assemblyOrderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Add finished bundle stock
            var bundleInventory = await _inventoryRepository.GetByVariantIdAsync(dto.BundleVariantId, cancellationToken);
            if (bundleInventory == null)
            {
                bundleInventory = new BatteryEC.Domain.Entities.Inventory.Inventory
                {
                    ProductVariantId = dto.BundleVariantId,
                    QuantityOnHand = dto.QuantityProduced,
                    QuantityReserved = 0,
                    ReorderLevel = 0,
                    Version = 1,
                    UpdatedAt = DateTime.UtcNow
                };
                await _inventoryRepository.AddAsync(bundleInventory, cancellationToken);
            }
            else
            {
                bundleInventory.QuantityOnHand += dto.QuantityProduced;
                bundleInventory.Version++;
                bundleInventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(bundleInventory);
            }

            await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
            {
                ProductVariantId = dto.BundleVariantId,
                TransactionType = "AssemblyProduction",
                QuantityChange = dto.QuantityProduced,
                ReferenceType = "AssemblyOrder",
                ReferenceId = order.AssemblyOrderId,
                UnitCost = order.UnitCost,
                CreatedByUserId = userId,
                Note = $"Produced from {order.AssemblyCode}",
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

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

    private static AssemblyOrderDto MapToDto(AssemblyOrder order)
    {
        return new AssemblyOrderDto
        {
            AssemblyOrderId = order.AssemblyOrderId,
            AssemblyCode = order.AssemblyCode,
            BundleVariantId = order.BundleVariantId,
            BundleVariantName = order.BundleVariant?.VariantName ?? string.Empty,
            QuantityProduced = order.QuantityProduced,
            TotalCost = order.TotalCost,
            UnitCost = order.UnitCost,
            Status = order.Status,
            Note = order.Note,
            CreatedByUserId = order.CreatedByUserId,
            AssembledAt = order.AssembledAt,
            Items = order.Items.Select(i => new AssemblyOrderItemDto
            {
                AssemblyOrderItemId = i.AssemblyOrderItemId,
                ComponentVariantId = i.ComponentVariantId,
                ComponentName = i.ComponentVariant?.VariantName ?? string.Empty,
                QuantityConsumed = i.QuantityConsumed,
                UnitCost = i.UnitCost,
                LineTotal = i.LineTotal
            }).ToList()
        };
    }
}
