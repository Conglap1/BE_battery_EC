using BatteryEC.Domain.Entities.Production;

namespace BatteryEC.Domain.Repositories;

public interface IAssemblyOrderRepository : IGenericRepository<AssemblyOrder>
{
    Task<AssemblyOrder?> GetByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default);
    Task<AssemblyOrder?> GetWithItemsAsync(long assemblyOrderId, CancellationToken cancellationToken = default);
}
