using System.Reflection;
using FlowStock.Application.Authentication;
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

        return services;
    }
}
