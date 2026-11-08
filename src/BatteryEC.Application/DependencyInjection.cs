using BatteryEC.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEC.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAssemblyService, AssemblyService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPosService, PosService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IRepairService, RepairService>();

        return services;
    }
}
