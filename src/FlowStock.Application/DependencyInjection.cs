using System.Reflection;
using FlowStock.Application.Authentication;
using FlowStock.Application.Catalog;
using FlowStock.Application.Inventory;
using FlowStock.Application.Production;
using FlowStock.Application.Traceability;
using FlowStock.Application.Users;
using FlowStock.Application.Warehouses;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FlowStock.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUnitOfMeasureService, UnitOfMeasureService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IStorageLocationService, StorageLocationService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<IStockMovementService, StockMovementService>();
        services.AddScoped<IBillOfMaterialService, BillOfMaterialService>();
        services.AddScoped<IProductionOrderService, ProductionOrderService>();
        services.AddScoped<ITraceabilityService, TraceabilityService>();

        return services;
    }
}
