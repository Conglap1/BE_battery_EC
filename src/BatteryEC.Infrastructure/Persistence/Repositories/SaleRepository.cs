using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class SaleRepository : GenericRepository<Sale>, ISaleRepository
{
    public SaleRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<Sale?> GetByCodeAsync(string saleCode, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Items)
                .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(s => s.SaleCode == saleCode, cancellationToken);
    }

    public async Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Items)
                .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(s => s.SaleId == saleId, cancellationToken);
    }
}
