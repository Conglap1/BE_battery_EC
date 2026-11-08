using System.Reflection;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Catalog;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Repositories;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BatteryEC.UnitTests.Controllers;

public class ProductsControllerTests
{
    [Fact]
    public void ProductsController_HasNoDirectDbContextDependency()
    {
        // P0.1 Verification: Ensure NO references to BatteryDbContext remain in ProductsController
        var fields = typeof(ProductsController).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var dbContextField = fields.FirstOrDefault(f => f.FieldType.Name.Contains("DbContext"));
        Assert.Null(dbContextField);

        var constructors = typeof(ProductsController).GetConstructors();
        foreach (var ctor in constructors)
        {
            var dbContextParam = ctor.GetParameters().FirstOrDefault(p => p.ParameterType.Name.Contains("DbContext"));
            Assert.Null(dbContextParam);
        }
    }

    [Fact]
    public void PublicProductVariantDto_HasNoCostPriceProperty()
    {
        // P0.2 Verification: ProductVariantDto must NOT expose CostPrice
        var costPriceProp = typeof(ProductVariantDto).GetProperty("CostPrice", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(costPriceProp);

        // AdminProductVariantDto MUST have CostPrice for internal back-office
        var adminCostPriceProp = typeof(AdminProductVariantDto).GetProperty("CostPrice", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(adminCostPriceProp);
    }

    [Fact]
    public async Task ProductsController_GetProducts_ReturnsPublicProductDtos()
    {
        var fakeProductService = new FakeProductService
        {
            Products = new List<ProductDto>
            {
                new()
                {
                    ProductId = 1,
                    Name = "Pin Lithium 12V 100Ah",
                    CategoryId = 2,
                    Variants = new List<ProductVariantDto>
                    {
                        new()
                        {
                            ProductVariantId = 10,
                            SKU = "PIN-12-100",
                            VariantName = "Standard",
                            SellingPrice = 5000000m,
                            AvailableStock = 15
                        }
                    }
                }
            }
        };

        var controller = new ProductsController(fakeProductService);
        var result = await controller.GetProducts(null, null, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<List<ProductDto>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Single(response.Data!);
        Assert.Equal(5000000m, response.Data![0].Variants[0].SellingPrice);
    }

    [Fact]
    public async Task ProductsController_GetProductById_WhenFound_ReturnsProductDto()
    {
        var fakeProductService = new FakeProductService
        {
            SingleProduct = new ProductDto
            {
                ProductId = 5,
                Name = "Sac Pin Thong Minh 12V",
                CategoryId = 1,
                Variants = new List<ProductVariantDto>
                {
                    new()
                    {
                        ProductVariantId = 50,
                        SKU = "SAC-12V",
                        SellingPrice = 450000m,
                        AvailableStock = 8
                    }
                }
            }
        };

        var controller = new ProductsController(fakeProductService);
        var result = await controller.GetProductById(5, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ProductDto>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(5, response.Data?.ProductId);
    }

    [Fact]
    public async Task ProductsController_GetProductById_WhenNotFound_ReturnsNotFound()
    {
        var fakeProductService = new FakeProductService { SingleProduct = null };
        var controller = new ProductsController(fakeProductService);

        var result = await controller.GetProductById(999, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ProductDto>>(notFoundResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task ProductService_MapToPublicDto_DoesNotLeakCostPrice_WhileAdminDtoContainsCostPrice()
    {
        // Genuine ProductService mapping test
        var fakeRepo = new FakeProductRepository
        {
            ProductList = new List<Product>
            {
                new()
                {
                    ProductId = 100,
                    ProductName = "Cell Pin EVE 280Ah",
                    CategoryId = 1,
                    IsActive = true,
                    Variants = new List<ProductVariant>
                    {
                        new()
                        {
                            ProductVariantId = 200,
                            ProductId = 100,
                            SKU = "EVE-280",
                            VariantName = "Grade A",
                            Price = 1800000m,
                            CostPrice = 1200000m, // Sensitive internal cost price
                            IsActive = true,
                            Inventory = new Inventory
                            {
                                QuantityOnHand = 50,
                                QuantityReserved = 5
                            }
                        }
                    }
                }
            }
        };

        var productService = new ProductService(fakeRepo);

        // 1. Public catalog query
        var publicResult = await productService.GetProductsAsync();
        Assert.Single(publicResult);
        var publicVariant = publicResult[0].Variants[0];
        Assert.Equal(1800000m, publicVariant.SellingPrice);
        Assert.Equal(45, publicVariant.AvailableStock);

        // 2. Admin catalog query
        var adminResult = await productService.GetAdminProductsAsync();
        Assert.Single(adminResult);
        var adminVariant = adminResult[0].Variants[0];
        Assert.Equal(1800000m, adminVariant.SellingPrice);
        Assert.Equal(1200000m, adminVariant.CostPrice); // Present in Admin DTO
    }

    private class FakeProductService : IProductService
    {
        public List<ProductDto> Products { get; set; } = new();
        public ProductDto? SingleProduct { get; set; }

        public Task<List<ProductDto>> GetProductsAsync(int? categoryId = null, string? search = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products);

        public Task<ProductDto?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SingleProduct);

        public Task<List<AdminProductDto>> GetAdminProductsAsync(int? categoryId = null, string? search = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<AdminProductDto>());

        public Task<AdminProductDto?> GetAdminProductByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminProductDto?>(null);
    }

    private class FakeProductRepository : IProductRepository
    {
        public List<Product> ProductList { get; set; } = new();

        public Task<IReadOnlyList<Product>> GetProductsAsync(int? categoryId = null, string? search = null, int? page = null, int? pageSize = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>(ProductList);

        public Task<Product?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductList.FirstOrDefault(p => p.ProductId == id));

        public Task<Product?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductList.FirstOrDefault());

        public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>(ProductList);

        public Task<IReadOnlyList<Product>> FindAsync(System.Linq.Expressions.Expression<Func<Product, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>(ProductList.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(Product entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Product entity) { }
        public void Remove(Product entity) { }
    }
}
