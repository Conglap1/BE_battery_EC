using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Assembly;
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

public class OperationalSecurityTests
{
    // =========================================================================
    // 1. OrdersController Security Tests (P0.3 & P0.4)
    // =========================================================================

    [Fact]
    public async Task OrdersController_CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakeOrderService = new FakeOrderService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var dto = new CreateOrderDto { RecipientName = "Test", RecipientPhone = "0900000000", ShippingAddress = "Addr" };
        var result = await controller.CreateOrder(dto, CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task OrdersController_CreateOrder_OverridesClientSuppliedUserId_WithJwtUserId()
    {
        CreateOrderDto? receivedDto = null;
        var fakeOrderService = new FakeOrderService
        {
            OnCreateOrder = dto =>
            {
                receivedDto = dto;
                return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-001", UserId = dto.UserId });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 77 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var dto = new CreateOrderDto { UserId = 9999, RecipientName = "Hacker", RecipientPhone = "0900000000", ShippingAddress = "Addr" };
        var result = await controller.CreateOrder(dto, CancellationToken.None);

        Assert.NotNull(receivedDto);
        Assert.Equal(77, receivedDto.UserId); // Must be JWT user ID, NOT 9999
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_WhenOwner_ReturnsOk()
    {
        var fakeOrderService = new FakeOrderService
        {
            ExistingOrder = new OrderDto { OrderId = 1, OrderCode = "ORD-OWNER-01", UserId = 50 }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 50, Roles = new List<string> { RoleType.User } };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-OWNER-01", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderDto>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("ORD-OWNER-01", response.Data?.OrderCode);
    }

    [Fact]
    public async Task OrdersController_GetOrderByCode_WhenNotOwnerAndNotElevated_ReturnsForbidden()
    {
        var fakeOrderService = new FakeOrderService
        {
            ExistingOrder = new OrderDto { OrderId = 1, OrderCode = "ORD-OTHER-01", UserId = 50 }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 99, Roles = new List<string> { RoleType.User } };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-OTHER-01", CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
        var response = Assert.IsType<ApiResponse<OrderDto>>(objResult.Value);
        Assert.False(response.Success);
    }

    [Theory]
    [InlineData(RoleType.Admin)]
    [InlineData(RoleType.StoreManager)]
    public async Task OrdersController_GetOrderByCode_WhenNotOwnerButElevatedRole_ReturnsOk(string elevatedRole)
    {
        var fakeOrderService = new FakeOrderService
        {
            ExistingOrder = new OrderDto { OrderId = 1, OrderCode = "ORD-ANY-01", UserId = 50 }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 999, Roles = new List<string> { elevatedRole } };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.GetOrderByCode("ORD-ANY-01", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<OrderDto>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("ORD-ANY-01", response.Data?.OrderCode);
    }

    [Fact]
    public async Task OrdersController_ConfirmOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakeOrderService = new FakeOrderService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.ConfirmOrder(1, CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task OrdersController_ConfirmOrder_UsesCurrentUserServiceUserId()
    {
        long capturedActorId = 0;
        var fakeOrderService = new FakeOrderService
        {
            OnConfirmOrder = (orderId, actorId) =>
            {
                capturedActorId = actorId;
                return Task.FromResult(true);
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 88 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.ConfirmOrder(10, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(88, capturedActorId);
    }

    [Fact]
    public async Task OrdersController_ShipOrder_UsesCurrentUserServiceUserId()
    {
        long capturedActorId = 0;
        var fakeOrderService = new FakeOrderService
        {
            OnShipOrder = (orderId, carrier, tracking, actorId) =>
            {
                capturedActorId = actorId;
                return Task.FromResult(true);
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 92 };
        var controller = new OrdersController(fakeOrderService, fakeCurrentUserService);

        var result = await controller.ShipOrder(10, "VNPost", "VN123456", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(92, capturedActorId);
    }

    // =========================================================================
    // 2. PosController Security Tests (P0.4)
    // =========================================================================

    [Fact]
    public async Task PosController_CreateSale_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakePosService = new FakePosService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new PosController(fakePosService, fakeCurrentUserService);

        var result = await controller.CreateSale(new CreateSaleDto(), CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SaleDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task PosController_CreateSale_UsesCurrentUserServiceUserId()
    {
        long capturedCashierId = 0;
        var fakePosService = new FakePosService
        {
            OnCreateSale = (dto, cashierId) =>
            {
                capturedCashierId = cashierId;
                return Task.FromResult(new SaleDto { SaleId = 1, SaleCode = "POS-001" });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 33 };
        var controller = new PosController(fakePosService, fakeCurrentUserService);

        var result = await controller.CreateSale(new CreateSaleDto(), CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<ApiResponse<SaleDto>>(createdResult.Value);
        Assert.True(response.Success);
        Assert.Equal(33, capturedCashierId);
    }

    // =========================================================================
    // 3. InventoryController Security Tests (P0.4)
    // =========================================================================

    [Fact]
    public async Task InventoryController_AdjustStock_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakeInventoryService = new FakeInventoryService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new InventoryController(fakeInventoryService, fakeCurrentUserService);

        var result = await controller.AdjustStock(new StockAdjustmentDto(), CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task InventoryController_AdjustStock_UsesCurrentUserServiceUserId()
    {
        long capturedUserId = 0;
        var fakeInventoryService = new FakeInventoryService
        {
            OnAdjustStock = (dto, userId) =>
            {
                capturedUserId = userId;
                return Task.FromResult(true);
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 44 };
        var controller = new InventoryController(fakeInventoryService, fakeCurrentUserService);

        var result = await controller.AdjustStock(new StockAdjustmentDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(44, capturedUserId);
    }

    // =========================================================================
    // 4. RepairsController Security Tests (P0.4)
    // =========================================================================

    [Fact]
    public async Task RepairsController_CreateRepairTicket_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakeRepairService = new FakeRepairService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new RepairsController(fakeRepairService, fakeCurrentUserService);

        var result = await controller.CreateRepairTicket(new CreateRepairTicketDto(), CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RepairTicketDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task RepairsController_CreateRepairTicket_UsesCurrentUserServiceUserId()
    {
        long capturedUserId = 0;
        var fakeRepairService = new FakeRepairService
        {
            OnCreateRepairTicket = (dto, userId) =>
            {
                capturedUserId = userId;
                return Task.FromResult(new RepairTicketDto { RepairTicketId = 1, TicketCode = "REP-001" });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 55 };
        var controller = new RepairsController(fakeRepairService, fakeCurrentUserService);

        var result = await controller.CreateRepairTicket(new CreateRepairTicketDto(), CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<ApiResponse<RepairTicketDto>>(createdResult.Value);
        Assert.True(response.Success);
        Assert.Equal(55, capturedUserId);
    }

    [Fact]
    public async Task RepairsController_AddRepairItem_UsesCurrentUserServiceUserId()
    {
        long capturedUserId = 0;
        var fakeRepairService = new FakeRepairService
        {
            OnAddRepairItem = (id, variantId, qty, price, userId) =>
            {
                capturedUserId = userId;
                return Task.FromResult(true);
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 55 };
        var controller = new RepairsController(fakeRepairService, fakeCurrentUserService);

        var result = await controller.AddRepairItem(1, 10, 2, 100000m, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(55, capturedUserId);
    }

    // =========================================================================
    // 5. AssemblyController Security Tests (P0.4)
    // =========================================================================

    [Fact]
    public async Task AssemblyController_ExecuteAssembly_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var fakeAssemblyService = new FakeAssemblyService();
        var fakeCurrentUserService = new FakeCurrentUserService { UserId = null };
        var controller = new AssemblyController(fakeAssemblyService, fakeCurrentUserService);

        var result = await controller.ExecuteAssembly(new CreateAssemblyOrderDto(), CancellationToken.None);

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<AssemblyOrderDto>>(unauthorizedResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task AssemblyController_ExecuteAssembly_UsesCurrentUserServiceUserId()
    {
        long capturedUserId = 0;
        var fakeAssemblyService = new FakeAssemblyService
        {
            OnExecuteAssembly = (dto, userId) =>
            {
                capturedUserId = userId;
                return Task.FromResult(new AssemblyOrderDto { AssemblyOrderId = 1, AssemblyCode = "ASM-001" });
            }
        };

        var fakeCurrentUserService = new FakeCurrentUserService { UserId = 66 };
        var controller = new AssemblyController(fakeAssemblyService, fakeCurrentUserService);

        var result = await controller.ExecuteAssembly(new CreateAssemblyOrderDto(), CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<ApiResponse<AssemblyOrderDto>>(createdResult.Value);
        Assert.True(response.Success);
        Assert.Equal(66, capturedUserId);
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
        public OrderDto? ExistingOrder { get; set; }
        public Func<CreateOrderDto, Task<OrderDto>>? OnCreateOrder { get; set; }
        public Func<long, long, Task<bool>>? OnConfirmOrder { get; set; }
        public Func<long, string, string, long, Task<bool>>? OnShipOrder { get; set; }

        public Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default)
        {
            if (OnCreateOrder != null) return OnCreateOrder(dto);
            return Task.FromResult(new OrderDto { OrderId = 1, OrderCode = "ORD-1", UserId = dto.UserId });
        }

        public Task<OrderDto?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingOrder);

        public Task<bool> ConfirmOrderAsync(long orderId, long confirmedByUserId, CancellationToken cancellationToken = default)
        {
            if (OnConfirmOrder != null) return OnConfirmOrder(orderId, confirmedByUserId);
            return Task.FromResult(true);
        }

        public Task<bool> ShipOrderAsync(long orderId, string carrierName, string trackingCode, long shippedByUserId, CancellationToken cancellationToken = default)
        {
            if (OnShipOrder != null) return OnShipOrder(orderId, carrierName, trackingCode, shippedByUserId);
            return Task.FromResult(true);
        }

        public Task<bool> CompleteOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class FakePosService : IPosService
    {
        public Func<CreateSaleDto, long, Task<SaleDto>>? OnCreateSale { get; set; }

        public Task<SaleDto> CreateSaleAsync(CreateSaleDto dto, long cashierUserId, CancellationToken cancellationToken = default)
        {
            if (OnCreateSale != null) return OnCreateSale(dto, cashierUserId);
            return Task.FromResult(new SaleDto { SaleId = 1, SaleCode = "POS-1" });
        }

        public Task<SaleDto?> GetSaleByCodeAsync(string saleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleDto?>(null);
    }

    private class FakeInventoryService : IInventoryService
    {
        public Func<StockAdjustmentDto, long, Task<bool>>? OnAdjustStock { get; set; }

        public Task<InventoryDto?> GetStockAsync(long variantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryDto?>(null);

        public Task<bool> AdjustStockAsync(StockAdjustmentDto dto, long userId, CancellationToken cancellationToken = default)
        {
            if (OnAdjustStock != null) return OnAdjustStock(dto, userId);
            return Task.FromResult(true);
        }

        public Task<bool> ReserveStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReleaseStockAsync(long variantId, int quantity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class FakeRepairService : IRepairService
    {
        public Func<CreateRepairTicketDto, long, Task<RepairTicketDto>>? OnCreateRepairTicket { get; set; }
        public Func<long, long, int, decimal, long, Task<bool>>? OnAddRepairItem { get; set; }

        public Task<RepairTicketDto> CreateRepairTicketAsync(CreateRepairTicketDto dto, long receivedByUserId, CancellationToken cancellationToken = default)
        {
            if (OnCreateRepairTicket != null) return OnCreateRepairTicket(dto, receivedByUserId);
            return Task.FromResult(new RepairTicketDto { RepairTicketId = 1, TicketCode = "REP-1" });
        }

        public Task<RepairTicketDto?> GetRepairTicketByCodeAsync(string ticketCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepairTicketDto?>(null);

        public Task<bool> AddRepairItemAsync(long ticketId, long componentVariantId, int quantity, decimal unitPrice, long technicianUserId, CancellationToken cancellationToken = default)
        {
            if (OnAddRepairItem != null) return OnAddRepairItem(ticketId, componentVariantId, quantity, unitPrice, technicianUserId);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateStatusAsync(long ticketId, string status, string? solutionNote, decimal? finalCost, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private class FakeAssemblyService : IAssemblyService
    {
        public Func<CreateAssemblyOrderDto, long, Task<AssemblyOrderDto>>? OnExecuteAssembly { get; set; }

        public Task<AssemblyOrderDto?> GetAssemblyOrderByCodeAsync(string assemblyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<AssemblyOrderDto?>(null);

        public Task<AssemblyOrderDto> ExecuteAssemblyOrderAsync(CreateAssemblyOrderDto dto, long userId, CancellationToken cancellationToken = default)
        {
            if (OnExecuteAssembly != null) return OnExecuteAssembly(dto, userId);
            return Task.FromResult(new AssemblyOrderDto { AssemblyOrderId = 1, AssemblyCode = "ASM-1" });
        }
    }
}
