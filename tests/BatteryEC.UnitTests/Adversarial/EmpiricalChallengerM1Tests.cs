using System.Reflection;
using System.Text.Json;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Assembly;
using BatteryEC.Application.DTOs.Catalog;
using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Application.DTOs.Repairs;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Repositories;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BatteryEC.UnitTests.Adversarial;

public class EmpiricalChallengerM1Tests
{
    // =========================================================================
    // 1. ProductVariantDto JSON Serialization & Data Masking Tests
    // =========================================================================

    [Fact]
    public void ProductVariantDto_SerializedJson_NeverContainsCostPrice_UnderAnyNamingPolicy()
    {
        var variant = new ProductVariantDto
        {
            ProductVariantId = 101,
            ProductId = 202,
            SKU = "PIN-LFP-12V",
            VariantName = "Lithium Iron Phosphate 12V 100Ah",
            OptionSummary = "12V - 100Ah",
            Unit = "Cái",
            WeightGrams = 11500,
            ImageUrl = "https://example.com/pin12v.jpg",
            SellingPrice = 5500000m,
            WarrantyMonths = 24,
            IsActive = true,
            AvailableStock = 42
        };

        var namingPolicies = new[]
        {
            null, // Default PascalCase
            JsonNamingPolicy.CamelCase,
            JsonNamingPolicy.SnakeCaseLower
        };

        foreach (var policy in namingPolicies)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = policy,
                WriteIndented = false
            };

            var json = JsonSerializer.Serialize(variant, options);

            // Assert neither costPrice nor CostPrice nor cost_price appears anywhere in JSON
            Assert.DoesNotContain("costPrice", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CostPrice", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cost_price", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cost", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProductDto_WithVariants_SerializedJson_NeverContainsCostPrice()
    {
        var productDto = new ProductDto
        {
            ProductId = 1,
            CategoryId = 2,
            CategoryName = "Pin Lithium",
            Brand = "EVE",
            Name = "Cell Pin EVE 280Ah",
            Slug = "cell-pin-eve-280ah",
            ShortDescription = "Cell pin dung lượng cao",
            Description = "Dùng cho xe điện và lưu trữ năng lượng mặt trời",
            IsPublished = true,
            IsFeatured = true,
            Variants = new List<ProductVariantDto>
            {
                new()
                {
                    ProductVariantId = 10,
                    ProductId = 1,
                    SKU = "EVE-280-A",
                    VariantName = "Grade A",
                    SellingPrice = 1800000m,
                    AvailableStock = 50,
                    IsActive = true
                },
                new()
                {
                    ProductVariantId = 11,
                    ProductId = 1,
                    SKU = "EVE-280-B",
                    VariantName = "Grade B",
                    SellingPrice = 1500000m,
                    AvailableStock = 20,
                    IsActive = true
                }
            }
        };

        var apiResponse = ApiResponse<ProductDto>.Ok(productDto);

        var jsonCamel = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var jsonDefault = JsonSerializer.Serialize(apiResponse);

        Assert.DoesNotContain("costprice", jsonCamel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costprice", jsonDefault, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicCatalogDtos_Reflection_HaveZeroCostPriceMembers()
    {
        var variantType = typeof(ProductVariantDto);
        var productType = typeof(ProductDto);

        var variantProps = variantType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var productProps = productType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        Assert.DoesNotContain(variantProps, p => p.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(productProps, p => p.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase));

        // Admin variant MUST contain CostPrice
        var adminVariantType = typeof(AdminProductVariantDto);
        var adminCostProp = adminVariantType.GetProperty("CostPrice");
        Assert.NotNull(adminCostProp);
    }

    [Fact]
    public async Task ProductService_PublicCatalogEndpoints_StrictlyStripSensitiveCostPrice()
    {
        var fakeRepo = new AdversarialFakeProductRepository
        {
            ProductList = new List<Product>
            {
                new()
                {
                    ProductId = 999,
                    ProductName = "Sensitive Wholesale Product",
                    CategoryId = 1,
                    IsActive = true,
                    Variants = new List<ProductVariant>
                    {
                        new()
                        {
                            ProductVariantId = 888,
                            ProductId = 999,
                            SKU = "SECRET-01",
                            VariantName = "Wholesale Batch",
                            Price = 10000000m,
                            CostPrice = 4500000m, // SENSITIVE INTERNAL COST PRICE
                            IsActive = true,
                            Inventory = new Inventory { QuantityOnHand = 100, QuantityReserved = 10 }
                        }
                    }
                }
            }
        };

        var productService = new ProductService(fakeRepo);

        // 1. Test GetProductsAsync()
        var publicList = await productService.GetProductsAsync();
        var serializedList = JsonSerializer.Serialize(publicList, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("4500000", serializedList);
        Assert.DoesNotContain("costPrice", serializedList, StringComparison.OrdinalIgnoreCase);

        // 2. Test GetProductByIdAsync()
        var singleProduct = await productService.GetProductByIdAsync(999);
        Assert.NotNull(singleProduct);
        var serializedSingle = JsonSerializer.Serialize(singleProduct, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("4500000", serializedSingle);
        Assert.DoesNotContain("costPrice", serializedSingle, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 2. Operational Controllers Query String & Authentication Adversarial Tests
    // =========================================================================

    private static readonly Type[] OperationalControllers = new[]
    {
        typeof(OrdersController),
        typeof(PosController),
        typeof(InventoryController),
        typeof(RepairsController),
        typeof(AssemblyController)
    };

    [Fact]
    public void AllOperationalControllers_RejectQueryStringUserIdOrCashierUserId()
    {
        // Reflection check across ALL methods of all 5 operational controllers
        foreach (var controllerType in OperationalControllers)
        {
            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();

                foreach (var param in parameters)
                {
                    var paramName = param.Name ?? string.Empty;

                    // Ensure no parameter is named userId, cashierUserId, or actorId with default values or query binding
                    var isForbiddenParamName = paramName.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
                                               paramName.Equals("cashierUserId", StringComparison.OrdinalIgnoreCase) ||
                                               paramName.Equals("actorId", StringComparison.OrdinalIgnoreCase);

                    if (isForbiddenParamName)
                    {
                        var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
                        Assert.True(fromQueryAttr == null,
                            $"Controller '{controllerType.Name}' method '{method.Name}' has forbidden query parameter '{paramName}'.");
                        Assert.False(param.HasDefaultValue,
                            $"Controller '{controllerType.Name}' method '{method.Name}' has default fallback value on security-sensitive parameter '{paramName}'.");
                    }
                }
            }
        }
    }

    [Fact]
    public void AllOperationalControllers_RequireAuthorizationAttribute()
    {
        foreach (var controllerType in OperationalControllers)
        {
            var classAuthAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            var hasClassAuth = classAuthAttr != null;

            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var methodAuthAttr = method.GetCustomAttribute<AuthorizeAttribute>();
                var methodAllowAnon = method.GetCustomAttribute<AllowAnonymousAttribute>();

                Assert.Null(methodAllowAnon); // No operational endpoint should ever allow anonymous
                Assert.True(hasClassAuth || methodAuthAttr != null,
                    $"Action '{controllerType.Name}.{method.Name}' lacks Authorize attribute.");
            }
        }
    }

    // =========================================================================
    // 3. Unauthenticated Caller Probe (Enforcing 401 Unauthorized)
    // =========================================================================

    [Fact]
    public async Task OrdersController_AllActorEndpoints_RejectUnauthenticatedCallers_With401()
    {
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };
        var controller = new OrdersController(new AdversarialFakeOrderService(), fakeCurrentUserService);

        // 1. CreateOrder
        var createResult = await controller.CreateOrder(new CreateOrderDto(), CancellationToken.None);
        var unauth1 = Assert.IsType<UnauthorizedObjectResult>(createResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth1.StatusCode);

        // 2. GetOrderByCode
        var getResult = await controller.GetOrderByCode("ORD-TEST", CancellationToken.None);
        var unauth2 = Assert.IsType<UnauthorizedObjectResult>(getResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth2.StatusCode);

        // 3. ConfirmOrder
        var confirmResult = await controller.ConfirmOrder(1, CancellationToken.None);
        var unauth3 = Assert.IsType<UnauthorizedObjectResult>(confirmResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth3.StatusCode);

        // 4. ShipOrder
        var shipResult = await controller.ShipOrder(1, "Carrier", "TRK-001", CancellationToken.None);
        var unauth4 = Assert.IsType<UnauthorizedObjectResult>(shipResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth4.StatusCode);
    }

    [Fact]
    public async Task PosController_CreateSale_RejectsUnauthenticatedCaller_With401()
    {
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };
        var controller = new PosController(new AdversarialFakePosService(), fakeCurrentUserService);

        var result = await controller.CreateSale(new CreateSaleDto(), CancellationToken.None);
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth.StatusCode);
    }

    [Fact]
    public async Task InventoryController_AdjustStock_RejectsUnauthenticatedCaller_With401()
    {
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };
        var controller = new InventoryController(new AdversarialFakeInventoryService(), fakeCurrentUserService);

        var result = await controller.AdjustStock(new StockAdjustmentDto(), CancellationToken.None);
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth.StatusCode);
    }

    [Fact]
    public async Task RepairsController_CreateRepairTicket_AndAddRepairItem_RejectUnauthenticatedCallers_With401()
    {
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };
        var controller = new RepairsController(new AdversarialFakeRepairService(), fakeCurrentUserService);

        // 1. CreateRepairTicket
        var createResult = await controller.CreateRepairTicket(new CreateRepairTicketDto(), CancellationToken.None);
        var unauth1 = Assert.IsType<UnauthorizedObjectResult>(createResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth1.StatusCode);

        // 2. AddRepairItem
        var addResult = await controller.AddRepairItem(1, 10, 1, 10000m, CancellationToken.None);
        var unauth2 = Assert.IsType<UnauthorizedObjectResult>(addResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth2.StatusCode);
    }

    [Fact]
    public async Task AssemblyController_ExecuteAssembly_RejectsUnauthenticatedCaller_With401()
    {
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };
        var controller = new AssemblyController(new AdversarialFakeAssemblyService(), fakeCurrentUserService);

        var result = await controller.ExecuteAssembly(new CreateAssemblyOrderDto(), CancellationToken.None);
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauth.StatusCode);
    }

    // =========================================================================
    // 4. Adversarial IDOR / Spoofing Stress Tests
    // =========================================================================

    [Fact]
    public async Task OrdersController_CreateOrder_AdversarialSpoofingPayload_EnforcesAuthenticatedUserId()
    {
        long capturedOrderUserId = -1;
        var fakeOrderService = new AdversarialFakeOrderService
        {
            OnCreateOrder = dto =>
            {
                capturedOrderUserId = dto.UserId;
                return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-SAFE", UserId = dto.UserId });
            }
        };

        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = 12345 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        // Adversary passes someone else's UserId in payload
        var maliciousDto = new CreateOrderDto
        {
            UserId = 999999, // Attempted victim ID
            RecipientName = "Attacker",
            RecipientPhone = "0987654321",
            ShippingAddress = "Attacker Address"
        };

        var result = await controller.CreateOrder(maliciousDto, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(12345, capturedOrderUserId); // MUST be token identity, NOT victim ID
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_BOLA_HorizontalPrivilegeEscalation_IsBlocked()
    {
        var fakeOrderService = new AdversarialFakeOrderService
        {
            ExistingOrder = new OrderDto { OrderId = 100, OrderCode = "ORD-VICTIM-100", UserId = 200 }
        };

        // Caller is authenticated User 999, trying to access User 200's order
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService
        {
            UserId = 999,
            Roles = new List<string> { RoleType.User }
        };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-VICTIM-100", CancellationToken.None);

        var forbiddenResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);
    }

    // =========================================================================
    // Fakes for Adversarial Testing
    // =========================================================================

    private class AdversarialFakeCurrentUserService : ICurrentUserService
    {
        public long? UserId { get; set; } = 1;
        public string? UserName { get; set; } = "challenger";
        public string? Email { get; set; } = "challenger@battery-ec.local";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { RoleType.User };
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private class AdversarialFakeProductRepository : IProductRepository
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

    private class AdversarialFakeOrderService : IOrderService
    {
        public OrderDto? ExistingOrder { get; set; }
        public Func<CreateOrderDto, Task<OrderDto>>? OnCreateOrder { get; set; }

        public Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
        {
            if (OnCreateOrder != null) return OnCreateOrder(dto);
            return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-TEST", UserId = dto.UserId });
        }

        public Task<OrderDto?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingOrder);

        public Task<bool> ConfirmOrderAsync(long orderId, long confirmedByUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ShipOrderAsync(long orderId, string carrierName, string trackingCode, long shippedByUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CompleteOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class AdversarialFakePosService : IPosService
    {
        public Task<SaleDto> CreateSaleAsync(CreateSaleDto dto, long cashierUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaleDto { SaleId = 1, SaleCode = "POS-TEST" });

        public Task<SaleDto?> GetSaleByCodeAsync(string saleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleDto?>(null);
    }

    private class AdversarialFakeInventoryService : IInventoryService
    {
        public Task<InventoryDto?> GetStockAsync(long variantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryDto?>(null);

        public Task<bool> AdjustStockAsync(StockAdjustmentDto dto, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReserveStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReleaseStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class AdversarialFakeRepairService : IRepairService
    {
        public Task<RepairTicketDto> CreateRepairTicketAsync(CreateRepairTicketDto dto, long receivedByUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RepairTicketDto { RepairTicketId = 1, TicketCode = "REP-TEST" });

        public Task<RepairTicketDto?> GetRepairTicketByCodeAsync(string ticketCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepairTicketDto?>(null);

        public Task<bool> AddRepairItemAsync(long ticketId, long componentVariantId, int quantity, decimal unitPrice, long technicianUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> UpdateStatusAsync(long ticketId, string status, string? solutionNote, decimal? finalCost, int? warrantyMonths = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class AdversarialFakeAssemblyService : IAssemblyService
    {
        public Task<AssemblyOrderDto?> GetAssemblyOrderByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<AssemblyOrderDto?>(null);

        public Task<AssemblyOrderDto> ExecuteAssemblyOrderAsync(CreateAssemblyOrderDto dto, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssemblyOrderDto { AssemblyOrderId = 1, AssemblyCode = "ASM-TEST" });
    }
}
