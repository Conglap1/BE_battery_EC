using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Application.DTOs.Reports;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Entities.Purchasing;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;
using BatteryEC.Domain.Enums;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Persistence.Dapper;
using BatteryEC.Infrastructure.Persistence.Dapper.Queries;
using BatteryEC.Infrastructure.Persistence.Repositories;
using BatteryEC.Infrastructure.Security;
using BatteryEC.Infrastructure.Services;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEC.UnitTests.Adversarial;

public class EmpiricalChallengerR5R8Tests : IAsyncLifetime
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    private readonly JwtSettings _jwtSettings = new()
    {
        SecretKey = "SuperSecretSecurityKeyBatteryECProject2026Min32BytesLength!",
        Issuer = "BatteryECWebApi",
        Audience = "BatteryECApp",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    private readonly PasswordHasher _passwordHasher = new();
    private readonly JwtTokenGenerator _jwtTokenGenerator;
    private readonly BatteryDbContext _context;
    private readonly AuthService _authService;
    private readonly List<long> _createdUserIds = new();
    private readonly List<long> _createdRepairTicketIds = new();

    public EmpiricalChallengerR5R8Tests()
    {
        _jwtTokenGenerator = new JwtTokenGenerator(Options.Create(_jwtSettings));

        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();
        _context = new BatteryDbContext(optionsBuilder.Options);

        _authService = new AuthService(_context, _passwordHasher, _jwtTokenGenerator);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdRepairTicketIds.Count > 0)
        {
            var tickets = await _context.RepairTickets.Where(t => _createdRepairTicketIds.Contains(t.RepairTicketId)).ToListAsync();
            _context.RepairTickets.RemoveRange(tickets);
            await _context.SaveChangesAsync();
        }

        if (_createdUserIds.Count > 0)
        {
            // First unlink customers created by auto-linking triggers
            var customers = await _context.Customers.Where(c => c.UserId.HasValue && _createdUserIds.Contains(c.UserId.Value)).ToListAsync();
            foreach (var c in customers)
            {
                c.UserId = null;
            }
            await _context.SaveChangesAsync();

            var users = await _context.Users.Where(u => _createdUserIds.Contains(u.UserId)).ToListAsync();
            _context.Users.RemoveRange(users);
            await _context.SaveChangesAsync();
        }

        await _context.DisposeAsync();
    }

    #region R5: Repair Service Warranty & Delivered Status

    [Fact]
    public void RepairTicketStatus_Includes_Quoted_And_Delivered()
    {
        Assert.Equal("Quoted", RepairTicketStatus.Quoted);
        Assert.Equal("Delivered", RepairTicketStatus.Delivered);
    }

    [Fact]
    public async Task RepairService_UpdateStatus_ToDelivered_UpdatesWarrantyAndDeliveryTimestamp()
    {
        // 1. Create intake ticket directly in DB with code <= 40 chars
        var ticket = new RepairTicket
        {
            TicketCode = $"REP-{Random.Shared.Next(100000, 999999)}",
            CustomerName = "Empirical Test Customer",
            CustomerPhone = "0988776655",
            DeviceModel = "Makita 18V Battery",
            IssueDescription = "Cells unbalanced",
            WarrantyMonths = 3,
            TicketStatus = "Repairing",
            PaymentStatus = "Unpaid",
            CreatedByUserId = 1,
            ReceivedAt = DateTime.UtcNow.AddDays(-2)
        };

        _context.RepairTickets.Add(ticket);
        await _context.SaveChangesAsync();
        _createdRepairTicketIds.Add(ticket.RepairTicketId);

        // 2. Invoke RepairService.UpdateStatusAsync with Delivered status and updated warrantyMonths
        var repairRepo = new RepairTicketRepository(_context);
        var invRepo = new InventoryRepository(_context);
        var variantRepo = new GenericRepository<ProductVariant>(_context);
        var uow = new UnitOfWork(_context);
        var service = new BatteryEC.Application.Services.RepairService(repairRepo, invRepo, variantRepo, uow);

        var success = await service.UpdateStatusAsync(
            ticketId: ticket.RepairTicketId,
            status: "Delivered",
            solutionNote: "Replaced 5 Sony VTC6 cells and rebalanced",
            finalCost: 450000m,
            warrantyMonths: 6);

        Assert.True(success);

        // 3. Verify changes persisted in PostgreSQL
        var updatedTicket = await _context.RepairTickets.FindAsync(ticket.RepairTicketId);
        Assert.NotNull(updatedTicket);
        Assert.Equal("Delivered", updatedTicket.TicketStatus);
        Assert.Equal("Replaced 5 Sony VTC6 cells and rebalanced", updatedTicket.SolutionNote);
        Assert.Equal(450000m, updatedTicket.FinalCost);
        Assert.Equal(6, updatedTicket.WarrantyMonths);
        Assert.NotNull(updatedTicket.DeliveredAt);
    }

    [Fact]
    public async Task RepairService_UpdateStatus_WithoutWarrantyMonths_PreservesExistingWarranty()
    {
        var ticket = new RepairTicket
        {
            TicketCode = $"REP-{Random.Shared.Next(100000, 999999)}",
            CustomerName = "Preserve Customer",
            CustomerPhone = "0988776644",
            DeviceModel = "DeWalt 20V",
            IssueDescription = "BMS faulty",
            WarrantyMonths = 12,
            TicketStatus = "Completed",
            PaymentStatus = "Paid",
            CreatedByUserId = 1,
            ReceivedAt = DateTime.UtcNow.AddDays(-3),
            CompletedAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.RepairTickets.Add(ticket);
        await _context.SaveChangesAsync();
        _createdRepairTicketIds.Add(ticket.RepairTicketId);

        var repairRepo = new RepairTicketRepository(_context);
        var invRepo = new InventoryRepository(_context);
        var variantRepo = new GenericRepository<ProductVariant>(_context);
        var uow = new UnitOfWork(_context);
        var service = new BatteryEC.Application.Services.RepairService(repairRepo, invRepo, variantRepo, uow);

        var success = await service.UpdateStatusAsync(
            ticketId: ticket.RepairTicketId,
            status: "Delivered",
            solutionNote: null,
            finalCost: null,
            warrantyMonths: null);

        Assert.True(success);

        var updatedTicket = await _context.RepairTickets.FindAsync(ticket.RepairTicketId);
        Assert.NotNull(updatedTicket);
        Assert.Equal("Delivered", updatedTicket.TicketStatus);
        Assert.Equal(12, updatedTicket.WarrantyMonths); // Preserved!
        Assert.NotNull(updatedTicket.DeliveredAt);
    }

    [Fact]
    public async Task WarrantyLookup_DeliveredRepairTicket_AppearsInViewResults()
    {
        var ticket = new RepairTicket
        {
            TicketCode = $"REP-VW-{Random.Shared.Next(100000, 999999)}",
            CustomerName = "View Lookup Customer",
            CustomerPhone = "0933112233",
            DeviceModel = "Bosch ProCore 18V",
            SerialOrPinCode = "BOSCH-PC-8899",
            IssueDescription = "Testing warranty view",
            WarrantyMonths = 6,
            TicketStatus = "Delivered",
            PaymentStatus = "Paid",
            CreatedByUserId = 1,
            ReceivedAt = DateTime.UtcNow.AddDays(-2),
            DeliveredAt = DateTime.UtcNow
        };

        _context.RepairTickets.Add(ticket);
        await _context.SaveChangesAsync();
        _createdRepairTicketIds.Add(ticket.RepairTicketId);

        var connectionFactory = new NpgsqlConnectionFactory(ConnectionString);
        var queryService = new WarrantyLookupQueryService(connectionFactory);

        var results = await queryService.QueryAsync("0933112233");
        Assert.NotEmpty(results);

        var match = results.FirstOrDefault(r => r.InvoiceCode == ticket.TicketCode);
        Assert.NotNull(match);
        Assert.Equal("RepairService", match.SaleChannel);
        Assert.Equal("View Lookup Customer", match.CustomerName);
        Assert.Equal(6, match.WarrantyMonths);
        Assert.Equal("Còn hạn bảo hành", match.WarrantyStatus);
    }

    #endregion

    #region R6: Financial P&L COGS and Profit Metrics

    [Fact]
    public void RevenueSummaryDto_CalculatesGrossAndNetMarginPercentages_Accurately()
    {
        var dto = new RevenueSummaryDto
        {
            GrossRevenue = 1000000m,
            TotalCogs = 600000m,
            GrossProfit = 400000m,
            DailyExpenseAmount = 100000m,
            NetProfit = 300000m
        };

        Assert.Equal(40.00m, dto.GrossMarginPercent);
        Assert.Equal(30.00m, dto.NetMarginPercent);
        Assert.Equal(dto.NetProfit, dto.RevenueAfterShopExpense); // Backward compatibility
    }

    [Fact]
    public void RevenueSummaryDto_HandlesZeroRevenue_WithoutDivisionByZero()
    {
        var dto = new RevenueSummaryDto
        {
            GrossRevenue = 0m,
            GrossProfit = 0m,
            DailyExpenseAmount = 500000m,
            NetProfit = -500000m
        };

        Assert.Equal(0m, dto.GrossMarginPercent);
        Assert.Equal(0m, dto.NetMarginPercent);
        Assert.Equal(-500000m, dto.RevenueAfterShopExpense);
    }

    [Fact]
    public void RevenueSummaryDto_HandlesNegativeNetProfit_Correctly()
    {
        var dto = new RevenueSummaryDto
        {
            GrossRevenue = 1000000m,
            TotalCogs = 800000m,
            GrossProfit = 200000m,
            DailyExpenseAmount = 500000m,
            NetProfit = -300000m
        };

        Assert.Equal(20.00m, dto.GrossMarginPercent);
        Assert.Equal(-30.00m, dto.NetMarginPercent);
    }

    [Fact]
    public async Task RevenueSummaryQueryService_LiveDatabase_QueriesAllCogsAndProfitFields()
    {
        var connectionFactory = new NpgsqlConnectionFactory(ConnectionString);
        var service = new RevenueSummaryQueryService(connectionFactory);

        var dailyList = await service.QueryDailyAsync();
        Assert.NotNull(dailyList);

        var aggregate = await service.GetAggregateSummaryAsync();
        Assert.NotNull(aggregate);
        Assert.True(aggregate.GrossRevenue >= 0);
        Assert.True(aggregate.TotalCogs >= 0);
    }

    #endregion

    #region R7: Auth Logout & Token Revocation

    [Fact]
    public async Task AuthService_LogoutAsync_SingleToken_RevokesTokenAndNullsExpiry()
    {
        var suffix = Random.Shared.Next(100000, 999999);
        var reg = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"logout_test_{suffix}",
            FullName = "Logout Single Test",
            Phone = $"097{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(reg.UserId);

        var token = reg.RefreshToken;
        Assert.NotNull(token);

        // Perform logout
        await _authService.LogoutAsync(token);

        // User record in DB should have null RefreshToken and null Expiry
        var userInDb = await _context.Users.FindAsync(reg.UserId);
        Assert.NotNull(userInDb);
        Assert.Null(userInDb.RefreshToken);
        Assert.Null(userInDb.RefreshTokenExpiryTime);

        // Attempting to refresh with the revoked token must throw UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshTokenAsync(token));
    }

    [Fact]
    public async Task AuthService_LogoutAsync_MultiToken_RevokesOnlySpecifiedToken()
    {
        var suffix = Random.Shared.Next(100000, 999999);
        var reg = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"multilogout_{suffix}",
            FullName = "Multi Logout Test",
            Phone = $"096{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(reg.UserId);

        var token1 = reg.RefreshToken!;

        // Rotate to get token2
        var refreshResp = await _authService.RefreshTokenAsync(token1);
        var token2 = refreshResp.RefreshToken!;
        Assert.NotEqual(token1, token2);

        // Verify DB contains both tokens separated by semicolon
        var userBefore = await _context.Users.FindAsync(reg.UserId);
        Assert.NotNull(userBefore);
        Assert.Contains(token1, userBefore.RefreshToken!);
        Assert.Contains(token2, userBefore.RefreshToken!);

        // Logout token1 only
        await _authService.LogoutAsync(token1);

        // Verify DB no longer contains token1, but still contains token2
        var userAfter = await _context.Users.FindAsync(reg.UserId);
        Assert.NotNull(userAfter);
        Assert.NotNull(userAfter.RefreshToken);
        Assert.DoesNotContain(token1, userAfter.RefreshToken);
        Assert.Contains(token2, userAfter.RefreshToken);

        // token1 cannot refresh
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshTokenAsync(token1));

        // token2 CAN still refresh
        var secondRefresh = await _authService.RefreshTokenAsync(token2);
        Assert.NotNull(secondRefresh);
        Assert.NotEmpty(secondRefresh.AccessToken);
    }

    [Fact]
    public async Task AuthService_LogoutAsync_ByUserId_ClearsAllTokens()
    {
        var suffix = Random.Shared.Next(100000, 999999);
        var reg = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"userlogout_{suffix}",
            FullName = "User Logout Test",
            Phone = $"095{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(reg.UserId);

        // Logout with null token but valid userId
        await _authService.LogoutAsync(null, reg.UserId);

        var userInDb = await _context.Users.FindAsync(reg.UserId);
        Assert.NotNull(userInDb);
        Assert.Null(userInDb.RefreshToken);
        Assert.Null(userInDb.RefreshTokenExpiryTime);
    }

    [Fact]
    public async Task AuthService_LogoutAsync_NonExistentToken_DoesNotThrow()
    {
        // Calling logout with unknown token should silently return
        await _authService.LogoutAsync("completely_bogus_token_xyz_123");
    }

    [Fact]
    public async Task AuthController_Logout_DeletesCookieWithStrictFlags()
    {
        var fakeAuthService = new FakeAuthServiceInternal();
        var fakeCurrentUserService = new FakeCurrentUserServiceInternal { UserId = 42 };
        var controller = new AuthController(fakeAuthService, fakeCurrentUserService, NullLogger<AuthController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = "refreshToken=cookie_token_abc";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Logout(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiResp.Success);

        // Verify Set-Cookie header contains deletion instructions
        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
        var setCookie = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("refreshToken=", setCookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region R8: EF Core Computed Columns ValueGeneratedOnAddOrUpdate

    [Fact]
    public void ComputedColumns_Configured_ValueGeneratedOnAddOrUpdate()
    {
        var model = _context.Model;

        // 1. AssemblyOrder.UnitCost
        var assemblyOrderEntity = model.FindEntityType(typeof(AssemblyOrder));
        Assert.NotNull(assemblyOrderEntity);
        var unitCostProp = assemblyOrderEntity.FindProperty(nameof(AssemblyOrder.UnitCost));
        Assert.NotNull(unitCostProp);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, unitCostProp.ValueGenerated);

        // 2. AssemblyOrderItem.LineTotal
        var assemblyOrderItemEntity = model.FindEntityType(typeof(AssemblyOrderItem));
        Assert.NotNull(assemblyOrderItemEntity);
        var lineTotalAoProp = assemblyOrderItemEntity.FindProperty(nameof(AssemblyOrderItem.LineTotal));
        Assert.NotNull(lineTotalAoProp);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, lineTotalAoProp.ValueGenerated);

        // 3. OrderItem.LineTotal
        var orderItemEntity = model.FindEntityType(typeof(OrderItem));
        Assert.NotNull(orderItemEntity);
        var lineTotalOiProp = orderItemEntity.FindProperty(nameof(OrderItem.LineTotal));
        Assert.NotNull(lineTotalOiProp);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, lineTotalOiProp.ValueGenerated);

        // 4. PurchaseReceiptItem.LineTotal
        var purchaseReceiptItemEntity = model.FindEntityType(typeof(PurchaseReceiptItem));
        Assert.NotNull(purchaseReceiptItemEntity);
        var lineTotalPriProp = purchaseReceiptItemEntity.FindProperty(nameof(PurchaseReceiptItem.LineTotal));
        Assert.NotNull(lineTotalPriProp);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, lineTotalPriProp.ValueGenerated);
    }

    #endregion

    #region Helpers & Fakes

    private class FakeAuthServiceInternal : IAuthService
    {
        public string? LoggedOutToken { get; private set; }
        public long? LoggedOutUserId { get; private set; }

        public Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AuthResponseDto> RefreshTokenAsync(string? refreshToken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UserProfileDto> GetCurrentUserAsync(long userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task LogoutAsync(string? refreshToken, long? userId = null, CancellationToken ct = default)
        {
            LoggedOutToken = refreshToken;
            LoggedOutUserId = userId;
            return Task.CompletedTask;
        }
    }

    private class FakeCurrentUserServiceInternal : ICurrentUserService
    {
        public long? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    #endregion
}
