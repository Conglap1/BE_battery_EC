using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Infrastructure;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task QueryProducts_WithVariants_SucceedsWithoutColumnErrors()
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        await using var context = new BatteryDbContext(optionsBuilder.Options);

        // Act - validates P1.9: Brand, ShortDescription, IsFeatured, OptionSummary, Unit, ImageUrl
        var products = await context.Products
            .Include(p => p.Variants)
            .ToListAsync();

        // Assert
        Assert.NotEmpty(products);
        var product = products.First();
        Assert.NotNull(product.ProductName);

        var variant = product.Variants.First();
        Assert.NotNull(variant.VariantName);
        Assert.NotNull(variant.Unit);
    }

    [Fact]
    public void CatalogConfigurations_PropertiesAreMapped_NotIgnored()
    {
        // Arrange
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();

        using var context = new BatteryDbContext(optionsBuilder.Options);
        var model = context.Model;

        // Act & Assert Product properties
        var productEntity = model.FindEntityType(typeof(Product));
        Assert.NotNull(productEntity);
        Assert.NotNull(productEntity.FindProperty(nameof(Product.Brand)));
        Assert.NotNull(productEntity.FindProperty(nameof(Product.ShortDescription)));
        Assert.NotNull(productEntity.FindProperty(nameof(Product.IsFeatured)));

        // Act & Assert ProductVariant properties
        var variantEntity = model.FindEntityType(typeof(ProductVariant));
        Assert.NotNull(variantEntity);
        Assert.NotNull(variantEntity.FindProperty(nameof(ProductVariant.OptionSummary)));
        Assert.NotNull(variantEntity.FindProperty(nameof(ProductVariant.Unit)));
        Assert.NotNull(variantEntity.FindProperty(nameof(ProductVariant.ImageUrl)));
    }

    [Fact]
    public void DateTimeProvider_ReturnsUtcNow_AndIsRegisteredInDI()
    {
        // 1. Direct unit test of DateTimeProvider
        IDateTimeProvider provider = new DateTimeProvider();
        var before = DateTime.UtcNow;
        var now = provider.UtcNow;
        var after = DateTime.UtcNow;

        Assert.True(now >= before && now <= after);

        // 2. DI registration test
        var services = new ServiceCollection();
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", ConnectionString },
            { "JwtSettings:Secret", "this-is-a-very-secure-and-long-secret-key-32bytes!" },
            { "JwtSettings:Issuer", "BatteryEC" },
            { "JwtSettings:Audience", "BatteryECClient" },
            { "JwtSettings:ExpiryMinutes", "60" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        var resolvedProvider = serviceProvider.GetService<IDateTimeProvider>();
        Assert.NotNull(resolvedProvider);
        Assert.IsType<DateTimeProvider>(resolvedProvider);
    }
}
