using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Enums;
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
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.BundleVariantId <= 0) throw new InvalidOperationException("Mã biến thể pack pin không hợp lệ.");
        if (dto.QuantityProduced <= 0) throw new InvalidOperationException("Số lượng pack pin sản xuất phải lớn hơn 0.");
        if (dto.Items == null || dto.Items.Count == 0) throw new InvalidOperationException("Lệnh lắp ráp phải có ít nhất một linh kiện tiêu hao.");

        foreach (var item in dto.Items)
        {
            if (item.ComponentVariantId <= 0) throw new InvalidOperationException("Mã linh kiện tiêu hao không hợp lệ.");
            if (item.QuantityConsumed <= 0) throw new InvalidOperationException($"Số lượng tiêu hao linh kiện #{item.ComponentVariantId} phải lớn hơn 0.");
            if (item.UnitCost < 0) throw new InvalidOperationException($"Đơn giá linh kiện #{item.ComponentVariantId} không được âm.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var bundleVariant = await _variantRepository.GetByIdAsync(dto.BundleVariantId, cancellationToken)
                ?? throw new InvalidOperationException($"Bundle variant #{dto.BundleVariantId} not found.");

            // 1. Kiểm tra tồn khả dụng (AvailableStock) cho toàn bộ linh kiện trước khi thực hiện
            var compInventoryMap = new Dictionary<long, BatteryEC.Domain.Entities.Inventory.Inventory>();
            foreach (var group in dto.Items.GroupBy(i => i.ComponentVariantId))
            {
                var compVariantId = group.Key;
                var totalConsumed = group.Sum(i => i.QuantityConsumed);

                var compInventory = await _inventoryRepository.GetByVariantIdAsync(compVariantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Không tìm thấy bản ghi tồn kho cho linh kiện #{compVariantId}.");

                if (compInventory.AvailableStock < totalConsumed)
                {
                    throw new InvalidOperationException($"Linh kiện #{compVariantId} không đủ tồn kho khả dụng để lắp ráp (Khả dụng: {compInventory.AvailableStock}, Yêu cầu: {totalConsumed}).");
                }

                compInventoryMap[compVariantId] = compInventory;
            }

            // 2. Khởi tạo đối tượng AssemblyOrder và chi tiết linh kiện
            decimal totalCost = 0;
            var order = new AssemblyOrder
            {
                AssemblyCode = string.IsNullOrWhiteSpace(dto.AssemblyCode)
                    ? $"ASM-{DateTime.UtcNow:yyyyMMddHHmmss}-{dto.BundleVariantId}"
                    : dto.AssemblyCode,
                BundleVariantId = dto.BundleVariantId,
                BundleVariant = bundleVariant,
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
                    ComponentVariant = componentVariant,
                    QuantityConsumed = item.QuantityConsumed,
                    UnitCost = item.UnitCost,
                    LineTotal = lineTotal
                });
            }

            order.TotalCost = totalCost;
            order.UnitCost = dto.QuantityProduced > 0 ? Math.Round(totalCost / dto.QuantityProduced, 2) : 0;

            // 3. LƯU ASSEMBLY ORDER TRƯỚC ĐỂ DB SINH RA AssemblyOrderId (Khắc phục ISSUE-02)
            await _assemblyOrderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 4. CẬP NHẬT GIÁ VỐN CHO PACK PIN THÀNH PHẨM (Khắc phục ISSUE-01)
            bundleVariant.CostPrice = order.UnitCost;
            bundleVariant.UpdatedAt = DateTime.UtcNow;
            _variantRepository.Update(bundleVariant);

            // 5. TRỪ KHO LINH KIỆN VÀ GHI THẺ KHO VỚI ReferenceId CHÍNH XÁC
            foreach (var item in order.Items)
            {
                var compInventory = compInventoryMap[item.ComponentVariantId];
                compInventory.QuantityOnHand -= item.QuantityConsumed;
                compInventory.Version++;
                compInventory.UpdatedAt = DateTime.UtcNow;
                _inventoryRepository.Update(compInventory);

                await _inventoryRepository.AddTransactionAsync(new InventoryTransaction
                {
                    ProductVariantId = item.ComponentVariantId,
                    TransactionType = InventoryTransactionType.AssemblyConsumption,
                    QuantityChange = -item.QuantityConsumed,
                    ReferenceType = "AssemblyOrder",
                    ReferenceId = order.AssemblyOrderId, // ID thực tế vừa được sinh
                    UnitCost = item.UnitCost,
                    CreatedByUserId = userId,
                    Note = $"Consumed in {order.AssemblyCode}",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            // 6. TĂNG KHO THÀNH PHẨM VÀ GHI THẺ KHO NHẬP PACK PIN
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
                TransactionType = InventoryTransactionType.AssemblyProduction,
                QuantityChange = dto.QuantityProduced,
                ReferenceType = "AssemblyOrder",
                ReferenceId = order.AssemblyOrderId, // ID thực tế vừa được sinh
                UnitCost = order.UnitCost,
                CreatedByUserId = userId,
                Note = $"Produced from {order.AssemblyCode}",
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

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
