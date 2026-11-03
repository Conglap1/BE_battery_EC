using System.Security.Claims;
using BatteryEC.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BatteryEC.UnitTests;

public class CurrentUserServiceTests
{
    private class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    [Fact]
    public void CurrentUserService_WhenConstructorNullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CurrentUserService(null!));
    }

    [Fact]
    public void CurrentUserService_WhenHttpContextIsNull_ReturnsNullAndDefaultValuesSafe()
    {
        var accessor = new FakeHttpContextAccessor { HttpContext = null };
        var service = new CurrentUserService(accessor);

        Assert.Null(service.UserId);
        Assert.Null(service.UserName);
        Assert.Null(service.Email);
        Assert.Empty(service.Roles);
        Assert.False(service.IsAuthenticated);
        Assert.False(service.IsInRole("Admin"));
        Assert.False(service.IsInRole(""));
    }

    [Fact]
    public void CurrentUserService_WhenUserIsUnauthenticated_ReturnsFalse()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var service = new CurrentUserService(accessor);

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.UserId);
        Assert.Null(service.UserName);
        Assert.Null(service.Email);
        Assert.Empty(service.Roles);
    }

    [Fact]
    public void CurrentUserService_WithStandardXmlClaimTypes_ExtractsAllProperties()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "105"),
            new Claim(ClaimTypes.Name, "manager"),
            new Claim(ClaimTypes.Email, "mgr@test.vn"),
            new Claim(ClaimTypes.Role, "StoreManager")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var service = new CurrentUserService(accessor);

        Assert.True(service.IsAuthenticated);
        Assert.Equal(105L, service.UserId);
        Assert.Equal("manager", service.UserName);
        Assert.Equal("mgr@test.vn", service.Email);
        Assert.Single(service.Roles);
        Assert.Contains("StoreManager", service.Roles);
        Assert.True(service.IsInRole("StoreManager"));
        Assert.False(service.IsInRole("Admin"));
    }

    [Fact]
    public void CurrentUserService_WithJwtShortClaimNames_ExtractsAllProperties()
    {
        var claims = new[]
        {
            new Claim("sub", "200"),
            new Claim("name", "warehouse_staff"),
            new Claim("email", "wh@test.vn"),
            new Claim("role", "WarehouseStaff")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var service = new CurrentUserService(accessor);

        Assert.True(service.IsAuthenticated);
        Assert.Equal(200L, service.UserId);
        Assert.Equal("warehouse_staff", service.UserName);
        Assert.Equal("wh@test.vn", service.Email);
        Assert.Single(service.Roles);
        Assert.Contains("WarehouseStaff", service.Roles);
        Assert.True(service.IsInRole("WarehouseStaff"));
    }

    [Fact]
    public void CurrentUserService_WithMultipleRoles_ReturnsDistinctCollection()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "Technician"),
            new Claim("role", "admin") // Case-insensitive duplicate
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var service = new CurrentUserService(accessor);

        Assert.Equal(2, service.Roles.Count);
        Assert.Contains("Admin", service.Roles);
        Assert.Contains("Technician", service.Roles);
        Assert.True(service.IsInRole("admin"));
        Assert.True(service.IsInRole("Admin"));
        Assert.True(service.IsInRole("technician"));
        Assert.False(service.IsInRole("SalesStaff"));
    }

    [Fact]
    public void CurrentUserService_WithNonNumericSub_ReturnsNullWithoutException()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "guid-abc-xyz-not-a-number")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var service = new CurrentUserService(accessor);

        Assert.Null(service.UserId);
    }
}
