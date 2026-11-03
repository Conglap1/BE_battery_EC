using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Catalog;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly BatteryDbContext _context;

    public ProductsController(BatteryDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetProducts(
        [FromQuery] int? categoryId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = _context.Products
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

        var products = await query
            .OrderBy(p => p.ProductId)
            .Select(p => new ProductDto
            {
                ProductId = p.ProductId,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.CategoryName : null,
                Brand = p.Brand,
                Name = p.ProductName,
                Slug = p.Slug,
                ShortDescription = p.ShortDescription,
                Description = p.Description,
                IsPublished = p.IsActive,
                IsFeatured = p.IsFeatured,
                Variants = p.Variants.Where(v => v.IsActive).Select(v => new ProductVariantDto
                {
                    ProductVariantId = v.ProductVariantId,
                    ProductId = v.ProductId,
                    SKU = v.SKU,
                    VariantName = v.VariantName,
                    OptionSummary = v.OptionSummary,
                    Unit = v.Unit,
                    WeightGrams = v.WeightGrams,
                    ImageUrl = v.ImageUrl,
                    SellingPrice = v.Price,
                    CostPrice = v.CostPrice,
                    WarrantyMonths = v.WarrantyMonths,
                    IsActive = v.IsActive,
                    AvailableStock = v.Inventory != null ? v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved : 0
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<ProductDto>>.Ok(products));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProductById(long id, CancellationToken cancellationToken)
    {
        var p = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive, cancellationToken);

        if (p == null)
        {
            return NotFound(ApiResponse<ProductDto>.Fail($"Product #{id} not found."));
        }

        var dto = new ProductDto
        {
            ProductId = p.ProductId,
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.CategoryName,
            Brand = p.Brand,
            Name = p.ProductName,
            Slug = p.Slug,
            ShortDescription = p.ShortDescription,
            Description = p.Description,
            IsPublished = p.IsActive,
            IsFeatured = p.IsFeatured,
            Variants = p.Variants.Where(v => v.IsActive).Select(v => new ProductVariantDto
            {
                ProductVariantId = v.ProductVariantId,
                ProductId = v.ProductId,
                SKU = v.SKU,
                VariantName = v.VariantName,
                OptionSummary = v.OptionSummary,
                Unit = v.Unit,
                WeightGrams = v.WeightGrams,
                ImageUrl = v.ImageUrl,
                SellingPrice = v.Price,
                CostPrice = v.CostPrice,
                WarrantyMonths = v.WarrantyMonths,
                IsActive = v.IsActive,
                AvailableStock = v.Inventory != null ? v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved : 0
            }).ToList()
        };

        return Ok(ApiResponse<ProductDto>.Ok(dto));
    }
}
