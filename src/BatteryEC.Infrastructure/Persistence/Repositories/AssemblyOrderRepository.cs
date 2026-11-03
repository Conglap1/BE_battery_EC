using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class AssemblyOrderRepository : GenericRepository<AssemblyOrder>, IAssemblyOrderRepository
{
    public AssemblyOrderRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<AssemblyOrder?> GetByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(ao => ao.BundleVariant)
            .Include(ao => ao.Items)
                .ThenInclude(i => i.ComponentVariant)
            .FirstOrDefaultAsync(ao => ao.AssemblyCode == assemblyCode, cancellationToken);
    }

    public async Task<AssemblyOrder?> GetWithItemsAsync(long assemblyOrderId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(ao => ao.BundleVariant)
            .Include(ao => ao.Items)
                .ThenInclude(i => i.ComponentVariant)
            .FirstOrDefaultAsync(ao => ao.AssemblyOrderId == assemblyOrderId, cancellationToken);
    }
}
