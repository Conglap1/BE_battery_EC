using BatteryEC.Domain.Entities.Pos;

namespace BatteryEC.Domain.Repositories;

public interface ISaleRepository : IGenericRepository<Sale>
{
    Task<Sale?> GetByCodeAsync(string saleCode, CancellationToken cancellationToken = default);
    Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default);
}
