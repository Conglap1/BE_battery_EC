using System.Linq.Expressions;
using System.Reflection;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.DTOs.Reports;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Repositories;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BatteryEC.UnitTests.Controllers;

public class ControllerRbacAttributeTests
{
    // =========================================================================
    // 1. ReportsController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void ReportsController_ShouldHaveAdminAuthorizeAttribute()
    {
        var authAttr = typeof(ReportsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal(RoleType.Admin, authAttr.Roles);
    }

    [Fact]
    public void ReportsController_Actions_ShouldNotAllowAnonymous()
    {
        var actions = typeof(ReportsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var action in actions)
        {
            var allowAnon = action.GetCustomAttribute<AllowAnonymousAttribute>();
            Assert.Null(allowAnon);
        }
    }

    // =========================================================================
    // 2. OrdersController RBAC Reflection & Behavior Tests
    // =========================================================================
    [Fact]
    public void OrdersController_ShouldHaveClassLevelAuthorizeAttribute()
    {
        var authAttr = typeof(OrdersController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
    }

    [Theory]
    [InlineData(nameof(OrdersController.ConfirmOrder))]
    [InlineData(nameof(OrdersController.ShipOrder))]
    [InlineData(nameof(OrdersController.CompleteOrder))]
    [InlineData(nameof(OrdersController.CancelOrder))]
    public void OrdersController_ManagementActions_ShouldRequireStoreManagerAndAdminRoles(string actionName)
    {
        var method = typeof(OrdersController).GetMethod(actionName);
        Assert.NotNull(method);

        var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.StoreManager, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    [Theory]
    [InlineData(nameof(OrdersController.CreateOrder))]
    [InlineData(nameof(OrdersController.GetOrderByCode))]
    public void OrdersController_CustomerActions_ShouldRequireAuthorizeWithoutRoleRestrictions(string actionName)
    {
        var method = typeof(OrdersController).GetMethod(actionName);
        Assert.NotNull(method);

        var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        // Customer actions should allow any authenticated user (Roles null or empty)
        Assert.True(string.IsNullOrEmpty(authAttr.Roles));

        var allowAnon = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.Null(allowAnon);
    }

    [Fact]
    public async Task OrdersController_CreateOrder_ShouldPopulateUserIdFromCurrentUserService_WhenDtoUserIdIsZeroOrNegative()
    {
        // Arrange
        CreateOrderDto? capturedDto = null;
        var fakeOrderService = new FakeOrderService
        {
            OnCreateOrder = dto =>
            {
                capturedDto = dto;
                return Task.FromResult(new OrderDto
                {
                    OrderId = 10,
                    OrderCode = "ORD-TEST-10",
                    UserId = dto.UserId
                });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 42 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var dto = new CreateOrderDto
        {
            UserId = 0,
            RecipientName = "Customer Test",
            RecipientPhone = "0901234567",
            ShippingAddress = "123 Street",
            PaymentMethod = "COD"
        };

        // Act
        var result = await controller.CreateOrder(dto, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderDto>>(createdResult.Value);
        Assert.True(response.Success);
        Assert.NotNull(capturedDto);
        Assert.Equal(42, capturedDto.UserId);
        Assert.Equal(42, response.Data?.UserId);
    }

    [Fact]
    public async Task OrdersController_CreateOrder_ShouldIgnoreClientUserId_AndEnforceCurrentUserServiceUserId()
    {
        // Arrange
        CreateOrderDto? capturedDto = null;
        var fakeOrderService = new FakeOrderService
        {
            OnCreateOrder = dto =>
            {
                capturedDto = dto;
                return Task.FromResult(new OrderDto
                {
                    OrderId = 11,
                    OrderCode = "ORD-TEST-11",
                    UserId = dto.UserId
                });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 42 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var dto = new CreateOrderDto
        {
            UserId = 99, // Insecure client attempt to impersonate another user
            RecipientName = "Customer Explicit",
            RecipientPhone = "0901234567",
            ShippingAddress = "456 Avenue",
            PaymentMethod = "COD"
        };

        // Act
        var result = await controller.CreateOrder(dto, CancellationToken.None);

        // Assert - Security P0.3: client UserId must be completely ignored and overridden by JWT identity
        Assert.NotNull(result);
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderDto>>(createdResult.Value);
        Assert.True(response.Success);
        Assert.NotNull(capturedDto);
        Assert.Equal(42, capturedDto.UserId);
        Assert.Equal(42, response.Data?.UserId);
    }

    // =========================================================================
    // 3. InventoryController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void InventoryController_ShouldHaveWarehouseStaffAndAdminAuthorizeAttribute()
    {
        var authAttr = typeof(InventoryController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.WarehouseStaff, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    // =========================================================================
    // 4. PosController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void PosController_ShouldHaveSalesStaffAndAdminAuthorizeAttribute()
    {
        var authAttr = typeof(PosController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.SalesStaff, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    // =========================================================================
    // 5. AssemblyController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void AssemblyController_ShouldHaveTechnicianAndAdminAuthorizeAttribute()
    {
        var authAttr = typeof(AssemblyController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.Technician, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    // =========================================================================
    // 6. RepairsController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void RepairsController_ShouldHaveTechnicianAndAdminAuthorizeAttribute()
    {
        var authAttr = typeof(RepairsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.NotNull(authAttr.Roles);
        Assert.Contains(RoleType.Technician, authAttr.Roles);
        Assert.Contains(RoleType.Admin, authAttr.Roles);
    }

    // =========================================================================
    // 7. ProductsController RBAC Reflection Tests
    // =========================================================================
    [Fact]
    public void ProductsController_ShouldHaveAllowAnonymousAttribute()
    {
        var allowAnon = typeof(ProductsController).GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(allowAnon);
    }

    // =========================================================================
    // 8. WarrantyController RBAC Reflection & Dual-Parameter Tests
    // =========================================================================
    [Fact]
    public void WarrantyController_ShouldHaveAllowAnonymousAttribute()
    {
        var classAllowAnon = typeof(WarrantyController).GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(classAllowAnon);

        var method = typeof(WarrantyController).GetMethod(nameof(WarrantyController.LookupWarranty));
        Assert.NotNull(method);
        var methodAllowAnon = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(methodAllowAnon);
    }

    [Theory]
    [InlineData("0901234567", null, "0901234567")]
    [InlineData(null, "SN-BAT-001", "SN-BAT-001")]
    [InlineData("0901234567", "SN-BAT-001", "0901234567")] // query takes precedence
    public async Task WarrantyController_LookupWarranty_ShouldSupportBothQueryAndSerialNumber(
        string? query,
        string? serialNumber,
        string expectedTerm)
    {
        string? receivedQuery = null;
        var fakeQueryService = new FakeWarrantyLookupQueryService
        {
            OnQuery = term =>
            {
                receivedQuery = term;
                return Task.FromResult<IReadOnlyList<WarrantyLookupDto>>(new List<WarrantyLookupDto>());
            }
        };

        var controller = new WarrantyController(fakeQueryService);
        var result = await controller.LookupWarranty(query, serialNumber, CancellationToken.None);

        Assert.NotNull(result);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<WarrantyLookupDto>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(expectedTerm, receivedQuery);
    }

    // =========================================================================
    // 9. AuthController Authorization Alignment Tests
    // =========================================================================
    [Fact]
    public void AuthController_Endpoints_ShouldHaveCorrectAuthorizationAttributes()
    {
        var registerMethod = typeof(AuthController).GetMethod(nameof(AuthController.Register));
        Assert.NotNull(registerMethod?.GetCustomAttribute<AllowAnonymousAttribute>());

        var loginMethod = typeof(AuthController).GetMethod(nameof(AuthController.Login));
        Assert.NotNull(loginMethod?.GetCustomAttribute<AllowAnonymousAttribute>());

        var refreshMethod = typeof(AuthController).GetMethod(nameof(AuthController.RefreshToken));
        Assert.NotNull(refreshMethod?.GetCustomAttribute<AllowAnonymousAttribute>());

        var meMethod = typeof(AuthController).GetMethod(nameof(AuthController.GetCurrentUser));
        Assert.NotNull(meMethod?.GetCustomAttribute<AuthorizeAttribute>());
    }

    // =========================================================================
    // 10. OrderService ConfirmOrderAsync Idempotency Test
    // =========================================================================
    [Fact]
    public async Task OrderService_ConfirmOrderAsync_ShouldBeIdempotentWhenAlreadyConfirmed()
    {
        var fakeRepo = new FakeOrderRepository();
        var existingOrder = new Order
        {
            OrderId = 2,
            OrderCode = "ORD-SEED-002",
            OrderStatus = "Confirmed",
            ConfirmedByUserId = 1,
            ConfirmedAt = DateTime.UtcNow.AddHours(-1)
        };
        fakeRepo.Orders[2] = existingOrder;

        var fakeUnitOfWork = new FakeUnitOfWork();
        var orderService = new OrderService(
            fakeRepo,
            new FakeInventoryRepository(),
            new FakeGenericRepository<ProductVariant>(),
            fakeUnitOfWork);

        // Act
        var result = await orderService.ConfirmOrderAsync(2, 1, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal("Confirmed", existingOrder.OrderStatus);
        // Verify SaveChanges was NOT called because it was already confirmed
        Assert.Equal(0, fakeUnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task OrderService_ConfirmOrderAsync_ShouldConfirmPendingOrder()
    {
        var fakeRepo = new FakeOrderRepository();
        var existingOrder = new Order
        {
            OrderId = 3,
            OrderCode = "ORD-SEED-003",
            OrderStatus = "PendingConfirmation"
        };
        fakeRepo.Orders[3] = existingOrder;

        var fakeUnitOfWork = new FakeUnitOfWork();
        var orderService = new OrderService(
            fakeRepo,
            new FakeInventoryRepository(),
            new FakeGenericRepository<ProductVariant>(),
            fakeUnitOfWork);

        // Act
        var result = await orderService.ConfirmOrderAsync(3, 5, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal("Confirmed", existingOrder.OrderStatus);
        Assert.Equal(5, existingOrder.ConfirmedByUserId);
        Assert.NotNull(existingOrder.ConfirmedAt);
        Assert.Equal(1, fakeUnitOfWork.SaveChangesCount);
    }

    // =========================================================================
    // Test Fakes
    // =========================================================================
    private class FakeCurrentUserService : ICurrentUserService
    {
        public long? UserId { get; set; } = 1;
        public string? UserName { get; set; } = "testuser";
        public string? Email { get; set; } = "test@batterypartsstore.vn";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { RoleType.User };
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private class FakeOrderService : IOrderService
    {
        public Func<CreateOrderDto, Task<OrderDto>>? OnCreateOrder { get; set; }

        public Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
        {
            if (OnCreateOrder != null) return OnCreateOrder(dto);
            return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-1", UserId = dto.UserId });
        }

        public Task<OrderDto?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDto?>(null);

        public Task<bool> ConfirmOrderAsync(long orderId, long confirmedByUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ShipOrderAsync(long orderId, string carrierName, string trackingCode, long shippedByUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CompleteOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class FakeWarrantyLookupQueryService : IWarrantyLookupQueryService
    {
        public Func<string?, Task<IReadOnlyList<WarrantyLookupDto>>>? OnQuery { get; set; }

        public Task<IReadOnlyList<WarrantyLookupDto>> QueryAsync(string? searchTerm, CancellationToken cancellationToken = default)
        {
            if (OnQuery != null) return OnQuery(searchTerm);
            return Task.FromResult<IReadOnlyList<WarrantyLookupDto>>(new List<WarrantyLookupDto>());
        }
    }

    private class FakeOrderRepository : IOrderRepository
    {
        public Dictionary<long, Order> Orders { get; } = new();

        public Task<Order?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            if (id is long longId && Orders.TryGetValue(longId, out var o)) return Task.FromResult<Order?>(o);
            return Task.FromResult<Order?>(null);
        }

        public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(Orders.Values.ToList());

        public Task<IReadOnlyList<Order>> FindAsync(Expression<Func<Order, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(Orders.Values.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(Order entity, CancellationToken cancellationToken = default)
        {
            Orders[entity.OrderId] = entity;
            return Task.CompletedTask;
        }

        public void Update(Order entity)
        {
            Orders[entity.OrderId] = entity;
        }

        public void Remove(Order entity)
        {
            Orders.Remove(entity.OrderId);
        }

        public Task<Order?> GetByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.Values.FirstOrDefault(o => o.OrderCode == orderCode));

        public Task<Order?> GetWithDetailsAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(orderId, out var o) ? o : null);
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeInventoryRepository : IInventoryRepository
    {
        public Task<Inventory?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Inventory?>(null);

        public Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(new List<Inventory>());

        public Task<IReadOnlyList<Inventory>> FindAsync(Expression<Func<Inventory, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Inventory>>(new List<Inventory>());

        public Task AddAsync(Inventory entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Inventory entity) { }
        public void Remove(Inventory entity) { }

        public Task<Inventory?> GetByVariantIdAsync(long variantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Inventory?>(null);

        public Task AddTransactionAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class FakeGenericRepository<T> : IGenericRepository<T> where T : class
    {
        public Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(null);

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(new List<T>());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(new List<T>());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(T entity) { }
        public void Remove(T entity) { }
    }
}
