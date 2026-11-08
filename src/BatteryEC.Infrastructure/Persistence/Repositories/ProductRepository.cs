using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence.Repositories;

public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    public ProductRepository(BatteryDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .Where(p => p.IsActive);

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p => EF.Functions.ILike(p.ProductName, $"%{s}%"));
        }

        query = query.OrderBy(p => p.ProductId);

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive, cancellationToken);
    }
}
