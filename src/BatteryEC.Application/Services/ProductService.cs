using BatteryEC.Application.DTOs.Catalog;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Repositories;

namespace BatteryEC.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;

    public ProductService(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<List<ProductDto>> GetProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetProductsAsync(categoryId, search, page, pageSize, cancellationToken);
        return products.Select(MapToPublicDto).ToList();
    }

    public async Task<ProductDto?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetProductByIdAsync(id, cancellationToken);
        return product == null ? null : MapToPublicDto(product);
    }

    public async Task<List<AdminProductDto>> GetAdminProductsAsync(
        int? categoryId = null,
        string? search = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetProductsAsync(categoryId, search, page, pageSize, cancellationToken);
        return products.Select(MapToAdminDto).ToList();
    }

    public async Task<AdminProductDto?> GetAdminProductByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetProductByIdAsync(id, cancellationToken);
        return product == null ? null : MapToAdminDto(product);
    }

    private static ProductDto MapToPublicDto(Product p)
    {
        return new ProductDto
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
                WarrantyMonths = v.WarrantyMonths,
                IsActive = v.IsActive,
                AvailableStock = v.Inventory != null ? v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved : 0
            }).ToList()
        };
    }

    private static AdminProductDto MapToAdminDto(Product p)
    {
        return new AdminProductDto
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
            Variants = p.Variants.Where(v => v.IsActive).Select(v => new AdminProductVariantDto
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
    }
}
