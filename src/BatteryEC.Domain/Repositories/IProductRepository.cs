using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Repositories;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<IReadOnlyList<Product>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    Task<Product?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default);
}
