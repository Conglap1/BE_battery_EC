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
using BatteryEC.Domain.Enums;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BatteryEC.UnitTests.Controllers;

/// <summary>
/// Empirical and adversarial test suite for Milestone 1 fixes (P0.1, P0.2, P0.3, P0.4).
/// Executed independently by Teamwork Empirical Challenger.
/// </summary>
public class AdversarialMilestone1ChallengerTests
{
    // =========================================================================
    // SECTION 1: ProductsController Reflection & Architecture Probes (P0.1)
    // =========================================================================

    [Fact]
    public void ProductsController_HasZeroFieldsReferencingDbContextOrEntityFrameworkCore()
    {
        var fields = typeof(ProductsController).GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in fields)
        {
            var fieldType = field.FieldType;
            Assert.False(
                fieldType.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase),
                $"Field '{field.Name}' has type '{fieldType.FullName}' which references DbContext.");

            Assert.False(
                fieldType.Namespace != null && fieldType.Namespace.StartsWith("Microsoft.EntityFrameworkCore"),
                $"Field '{field.Name}' has type '{fieldType.FullName}' belonging to Microsoft.EntityFrameworkCore.");
        }
    }

    [Fact]
    public void ProductsController_HasZeroPropertiesReferencingDbContextOrEntityFrameworkCore()
    {
        var properties = typeof(ProductsController).GetProperties(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var prop in properties)
        {
            var propType = prop.PropertyType;
            Assert.False(
                propType.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase),
                $"Property '{prop.Name}' has type '{propType.FullName}' which references DbContext.");

            Assert.False(
                propType.Namespace != null && propType.Namespace.StartsWith("Microsoft.EntityFrameworkCore"),
                $"Property '{prop.Name}' has type '{propType.FullName}' belonging to Microsoft.EntityFrameworkCore.");
        }
    }

    [Fact]
    public void ProductsController_HasZeroConstructorsReferencingDbContextOrEntityFrameworkCore()
    {
        var constructors = typeof(ProductsController).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);

        foreach (var ctor in constructors)
        {
            foreach (var param in ctor.GetParameters())
            {
                var paramType = param.ParameterType;
                Assert.False(
                    paramType.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase),
                    $"Constructor parameter '{param.Name}' has type '{paramType.FullName}' which references DbContext.");

                Assert.False(
                    paramType.Namespace != null && paramType.Namespace.StartsWith("Microsoft.EntityFrameworkCore"),
                    $"Constructor parameter '{param.Name}' has type '{paramType.FullName}' belonging to Microsoft.EntityFrameworkCore.");
            }
        }
    }

    [Fact]
    public void ProductsController_HasZeroMethodsReferencingDbContextOrEntityFrameworkCore()
    {
        var methods = typeof(ProductsController).GetMethods(
            BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            // Return type check
            Assert.False(
                method.ReturnType.Namespace != null && method.ReturnType.Namespace.StartsWith("Microsoft.EntityFrameworkCore"),
                $"Method '{method.Name}' has return type belonging to Microsoft.EntityFrameworkCore.");

            // Parameter checks
            foreach (var param in method.GetParameters())
            {
                Assert.False(
                    param.ParameterType.Namespace != null && param.ParameterType.Namespace.StartsWith("Microsoft.EntityFrameworkCore"),
                    $"Method '{method.Name}' parameter '{param.Name}' belongs to Microsoft.EntityFrameworkCore.");
            }
        }
    }

    // =========================================================================
    // SECTION 2: Public Product DTO & CostPrice Leakage Probes (P0.2)
    // =========================================================================

    [Fact]
    public void ProductVariantDto_HasNoCostPriceProperty_UnderAnyBindingFlags()
    {
        var costPriceProp = typeof(ProductVariantDto).GetProperty(
            "CostPrice",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

        Assert.Null(costPriceProp);
    }

    [Fact]
    public void ProductDto_SerializedToJson_ContainsNoCostPriceKeys()
    {
        var publicDto = new ProductDto
        {
            ProductId = 1,
            Name = "Battery Pack 48V",
            Variants = new List<ProductVariantDto>
            {
                new()
                {
                    ProductVariantId = 10,
                    VariantName = "Standard",
                    SKU = "BAT-48V",
                    SellingPrice = 12000000m,
                    AvailableStock = 5
                }
            }
        };

        var json = JsonSerializer.Serialize(publicDto);
        Assert.DoesNotContain("costprice", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminProductVariantDto_ExplicitlyRetainsCostPrice()
    {
        var adminCostPriceProp = typeof(AdminProductVariantDto).GetProperty("CostPrice", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(adminCostPriceProp);
        Assert.Equal(typeof(decimal?), adminCostPriceProp.PropertyType);
    }

    // =========================================================================
    // SECTION 3: CreateOrder Spoofed UserId Adversarial Probes (P0.3)
    // =========================================================================

    [Theory]
    [InlineData(1)]        // Attempt to impersonate Admin
    [InlineData(999999)]   // Attempt to assign to arbitrary user
    [InlineData(-100)]     // Attempt negative user ID injection
    [InlineData(0)]        // Attempt zero ID injection
    public async Task OrdersController_CreateOrder_AdversarialSpoofedUserId_IsOverriddenByJwtUserId(long spoofedUserId)
    {
        CreateOrderDto? receivedDto = null;
        var fakeOrderService = new AdversarialFakeOrderService
        {
            OnCreateOrder = dto =>
            {
                receivedDto = dto;
                return Task.FromResult(new OrderDto
                {
                    OrderId = 100,
                    OrderCode = "ORD-ADV-001",
                    UserId = dto.UserId
                });
            }
        };

        const long authenticatedActorId = 42;
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = authenticatedActorId };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var maliciousDto = new CreateOrderDto
        {
            UserId = spoofedUserId, // Spoofed attack payload
            RecipientName = "Attacker",
            RecipientPhone = "0901234567",
            ShippingAddress = "Unknown Road",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductVariantId = 1, Quantity = 2 }
            }
        };

        var result = await controller.CreateOrder(maliciousDto, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var apiResponse = Assert.IsType<ApiResponse<OrderDto>>(createdResult.Value);

        Assert.True(apiResponse.Success);
        Assert.NotNull(receivedDto);
        // CRITICAL ASSERTION: The spoofed ID must be completely erased and overridden with authenticated JWT actor ID
        Assert.Equal(authenticatedActorId, receivedDto.UserId);
        Assert.NotEqual(spoofedUserId, receivedDto.UserId);
        Assert.Equal(authenticatedActorId, apiResponse.Data?.UserId);
    }

    [Fact]
    public async Task OrdersController_CreateOrder_WhenUnauthenticated_RejectsWith401_AndServiceNeverInvoked()
    {
        bool serviceWasCalled = false;
        var fakeOrderService = new AdversarialFakeOrderService
        {
            OnCreateOrder = dto =>
            {
                serviceWasCalled = true;
                return Task.FromResult(new OrderDto());
            }
        };

        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null }; // Unauthenticated
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var dto = new CreateOrderDto
        {
            UserId = 1,
            RecipientName = "Ghost",
            RecipientPhone = "0900000000",
            ShippingAddress = "Nowhere"
        };

        var result = await controller.CreateOrder(dto, CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
        Assert.False(serviceWasCalled, "OrderService MUST NOT be called when actor is unauthenticated.");
    }

    // =========================================================================
    // SECTION 4: GetOrderByCode IDOR / BOLA Attack Probes (P0.3)
    // =========================================================================

    [Theory]
    [InlineData("Customer")]
    [InlineData("User")]
    [InlineData("Technician")]
    [InlineData("Cashier")]
    public async Task OrdersController_GetOrderByCode_IDOR_NonOwnerWithoutElevatedRole_Receives403Forbidden(string victimlessRole)
    {
        const long victimUserId = 100;
        const long attackerUserId = 200;

        var fakeOrderService = new AdversarialFakeOrderService
        {
            ExistingOrder = new OrderDto
            {
                OrderId = 55,
                OrderCode = "ORD-VICTIM-PRIVATE-01",
                UserId = victimUserId, // Belongs to victim
                RecipientName = "Victim VIP",
                TotalAmount = 50000000m
            }
        };

        var fakeCurrentUserService = new AdversarialFakeCurrentUserService
        {
            UserId = attackerUserId, // Attacker identity
            Roles = new List<string> { victimlessRole }
        };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-VICTIM-PRIVATE-01", CancellationToken.None);

        // Assert 403 Forbidden
        var forbiddenResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(forbiddenResult.Value);
        Assert.False(response.Success);
        Assert.Contains("permission", response.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_LegitimateOwner_Receives200Ok()
    {
        const long legitimateOwnerId = 100;

        var fakeOrderService = new AdversarialFakeOrderService
        {
            ExistingOrder = new OrderDto
            {
                OrderId = 55,
                OrderCode = "ORD-MY-ORDER-01",
                UserId = legitimateOwnerId,
                RecipientName = "Alice",
                TotalAmount = 1500000m
            }
        };

        var fakeCurrentUserService = new AdversarialFakeCurrentUserService
        {
            UserId = legitimateOwnerId,
            Roles = new List<string> { RoleType.User }
        };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-MY-ORDER-01", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("ORD-MY-ORDER-01", response.Data?.OrderCode);
        Assert.Equal(legitimateOwnerId, response.Data?.UserId);
    }

    [Theory]
    [InlineData(RoleType.Admin)]
    [InlineData(RoleType.StoreManager)]
    public async Task OrdersController_GetOrderByCode_ElevatedRoles_CanInspectAnyOrder(string elevatedRole)
    {
        const long customerUserId = 500;
        const long staffUserId = 900;

        var fakeOrderService = new AdversarialFakeOrderService
        {
            ExistingOrder = new OrderDto
            {
                OrderId = 77,
                OrderCode = "ORD-CUSTOMER-500",
                UserId = customerUserId,
                TotalAmount = 9900000m
            }
        };

        var fakeCurrentUserService = new AdversarialFakeCurrentUserService
        {
            UserId = staffUserId,
            Roles = new List<string> { elevatedRole }
        };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-CUSTOMER-500", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("ORD-CUSTOMER-500", response.Data?.OrderCode);
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_WhenOrderNotFound_Returns404NotFound()
    {
        var fakeOrderService = new AdversarialFakeOrderService { ExistingOrder = null };
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService
        {
            UserId = 10,
            Roles = new List<string> { RoleType.User }
        };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-NONEXISTENT", CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(notFoundResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_WhenUnauthenticated_Returns401Unauthorized()
    {
        var fakeOrderService = new AdversarialFakeOrderService();
        var fakeCurrentUserService = new AdversarialFakeCurrentUserService { UserId = null };

        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-ANY", CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var response = Assert.IsType<ApiResponse<OrderDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    // =========================================================================
    // SECTION 5: Parameter Reflection Probes on Operational Controllers (P0.4)
    // =========================================================================

    [Theory]
    [InlineData(typeof(OrdersController))]
    [InlineData(typeof(PosController))]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(RepairsController))]
    [InlineData(typeof(AssemblyController))]
    public void OperationalControllers_ActionMethods_HaveNoDefaultFallbackUserParameters(Type controllerType)
    {
        var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                var paramName = parameter.Name?.ToLowerInvariant() ?? "";

                // Parameter must not be named userId or cashierUserId with a default value
                if (paramName is "userid" or "cashieruserid" or "actorid")
                {
                    Assert.False(
                        parameter.HasDefaultValue,
                        $"Method '{controllerType.Name}.{method.Name}' has parameter '{parameter.Name}' with a default fallback value ({parameter.DefaultValue}). Actor ID must be extracted from ICurrentUserService!");
                }
            }
        }
    }

    [Theory]
    [InlineData(typeof(PosController))]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(RepairsController))]
    [InlineData(typeof(AssemblyController))]
    public void OperationalControllers_InjectICurrentUserServiceInConstructor(Type controllerType)
    {
        var constructors = controllerType.GetConstructors();
        Assert.NotEmpty(constructors);

        foreach (var ctor in constructors)
        {
            var hasCurrentUserService = ctor.GetParameters()
                .Any(p => p.ParameterType == typeof(ICurrentUserService));

            Assert.True(
                hasCurrentUserService,
                $"Controller '{controllerType.Name}' constructor does not inject ICurrentUserService.");
        }
    }

    // =========================================================================
    // Test Fakes
    // =========================================================================

    private class AdversarialFakeCurrentUserService : ICurrentUserService
    {
        public long? UserId { get; set; } = 1;
        public string? UserName { get; set; } = "challenger";
        public string? Email { get; set; } = "challenger@batterypartsstore.vn";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { RoleType.User };
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private class AdversarialFakeOrderService : IOrderService
    {
        public OrderDto? ExistingOrder { get; set; }
        public Func<CreateOrderDto, Task<OrderDto>>? OnCreateOrder { get; set; }

        public Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
        {
            if (OnCreateOrder != null) return OnCreateOrder(dto);
            return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-DEFAULT", UserId = dto.UserId });
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
}
