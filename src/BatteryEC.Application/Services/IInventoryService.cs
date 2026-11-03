using BatteryEC.Application.DTOs.Inventory;

namespace BatteryEC.Application.Services;

public interface IInventoryService
{
    Task<InventoryDto?> GetStockAsync(long variantId, CancellationToken cancellationToken = default);
    Task<bool> AdjustStockAsync(StockAdjustmentDto dto, long userId, CancellationToken cancellationToken = default);
    Task<bool> ReserveStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default);
    Task<bool> ReleaseStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default);
}
