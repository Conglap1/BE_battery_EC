using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class InventoryRepository : GenericRepository<BatteryEC.Domain.Entities.Inventory.Inventory>, IInventoryRepository
{
    public InventoryRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<BatteryEC.Domain.Entities.Inventory.Inventory?> GetByVariantIdAsync(long variantId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(i => i.ProductVariant)
            .FirstOrDefaultAsync(i => i.ProductVariantId == variantId, cancellationToken);
    }

    public async Task AddTransactionAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _context.InventoryTransactions.AddAsync(transaction, cancellationToken);
    }
}
