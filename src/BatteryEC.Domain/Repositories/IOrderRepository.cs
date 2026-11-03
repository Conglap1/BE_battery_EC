using BatteryEC.Domain.Entities.Sales;

namespace BatteryEC.Domain.Repositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByCodeAsync(string orderCode, CancellationToken cancellationToken = default);
    Task<Order?> GetWithDetailsAsync(long orderId, CancellationToken cancellationToken = default);
}
