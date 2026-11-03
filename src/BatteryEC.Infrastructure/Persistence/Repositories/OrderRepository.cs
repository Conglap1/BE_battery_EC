using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<Order?> GetByCodeAsync(string orderCode, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductVariant)
            .Include(o => o.Payments)
            .Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.OrderCode == orderCode, cancellationToken);
    }

    public async Task<Order?> GetWithDetailsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductVariant)
            .Include(o => o.Payments)
            .Include(o => o.Shipments)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);
    }
}
