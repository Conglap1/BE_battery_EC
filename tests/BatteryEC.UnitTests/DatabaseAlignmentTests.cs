using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BatteryEC.UnitTests;

public class DatabaseAlignmentTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    [Fact]
    public async Task QueryUsers_WithRoles_SucceedsWithoutColumnErrors()
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        await using var context = new BatteryDbContext(optionsBuilder.Options);

        // Act - this will fail if refresh_token, refresh_token_expiry_time, or role_name are misaligned
        var users = await context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(users);

        var adminUser = users.FirstOrDefault(u => u.Username == "admin");
        Assert.NotNull(adminUser);
        Assert.Equal("admin", adminUser.Username);
        Assert.Equal("admin@batterypartsstore.vn", adminUser.Email);
        Assert.NotEmpty(adminUser.UserRoles);

        var adminRole = adminUser.UserRoles.First().Role;
        Assert.NotNull(adminRole);
        Assert.Equal("Admin", adminRole.Name);
    }
}
