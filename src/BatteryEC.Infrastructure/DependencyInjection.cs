using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Domain.Repositories;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Persistence.Dapper;
using BatteryEC.Infrastructure.Persistence.Dapper.Queries;
using BatteryEC.Infrastructure.Persistence.Repositories;
using BatteryEC.Infrastructure.Security;
using BatteryEC.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEC.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in configuration.");

        // EF Core BatteryDbContext with Npgsql and snake_case naming convention
        services.AddDbContext<BatteryDbContext>(options =>
        {
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention();
        });

        // Dapper IDbConnectionFactory
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>(_ =>
            new NpgsqlConnectionFactory(connectionString));

        // Dapper View Query Services
        services.AddScoped<IWarrantyLookupQueryService, WarrantyLookupQueryService>();
        services.AddScoped<IRevenueSummaryQueryService, RevenueSummaryQueryService>();
        services.AddScoped<IInventoryReconciliationQueryService, InventoryReconciliationQueryService>();
        services.AddScoped<ISupplierDebtQueryService, SupplierDebtQueryService>();

        // Unit of Work & Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IAssemblyOrderRepository, AssemblyOrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IRepairTicketRepository, RepairTicketRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();

        // Security & Cryptography Services
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // HttpContextAccessor & User/Auth Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
