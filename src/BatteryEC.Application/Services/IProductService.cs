using BatteryEC.Application.DTOs.Catalog;

namespace BatteryEC.Application.Services;

public interface IProductService
{
    Task<List<ProductDto>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<List<AdminProductDto>> GetAdminProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    Task<AdminProductDto?> GetAdminProductByIdAsync(long id, CancellationToken cancellationToken = default);
}
