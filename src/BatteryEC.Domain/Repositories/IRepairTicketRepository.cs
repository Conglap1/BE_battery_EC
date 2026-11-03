using BatteryEC.Domain.Entities.Services;

namespace BatteryEC.Domain.Repositories;

public interface IRepairTicketRepository : IGenericRepository<RepairTicket>
{
    Task<RepairTicket?> GetByCodeAsync(string ticketCode, CancellationToken cancellationToken = default);
    Task<RepairTicket?> GetWithItemsAsync(long repairTicketId, CancellationToken cancellationToken = default);
}
