using BatteryEC.Domain.Entities.Inventory;

namespace BatteryEC.Domain.Repositories;

public interface IInventoryRepository : IGenericRepository<Inventory>
{
    Task<Inventory?> GetByVariantIdAsync(long variantId, CancellationToken cancellationToken = default);
    Task AddTransactionAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default);
}
