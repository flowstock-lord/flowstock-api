using System.Reflection;
using FlowStock.Application.Authentication;
using FlowStock.Application.Catalog;
using FlowStock.Application.Users;
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

        return services;
    }
}
