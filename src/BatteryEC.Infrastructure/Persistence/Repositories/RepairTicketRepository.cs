using BatteryEC.Domain.Entities.Services;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class RepairTicketRepository : GenericRepository<RepairTicket>, IRepairTicketRepository
{
    public RepairTicketRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<RepairTicket?> GetByCodeAsync(string ticketCode, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(rt => rt.Items)
                .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(rt => rt.TicketCode == ticketCode, cancellationToken);
    }

    public async Task<RepairTicket?> GetWithItemsAsync(long repairTicketId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(rt => rt.Items)
                .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(rt => rt.RepairTicketId == repairTicketId, cancellationToken);
    }
}
