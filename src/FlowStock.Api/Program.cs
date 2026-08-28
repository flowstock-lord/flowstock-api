using FlowStock.Api.Middleware;
using FlowStock.Application;
using FlowStock.Infrastructure;
using FlowStock.Infrastructure.Persistence;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new() { Title = "FlowStock API", Version = "v1" });
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddHealthChecks()
        .AddNpgSql(
            connectionStringFactory: sp =>
                sp.GetRequiredService<IConfiguration>()
                    .GetConnectionString(FlowStock.Infrastructure.DependencyInjection.ConnectionStringName)!,
            name: "postgresql",
            tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "FlowStock API v1"));
    }

    // TLS is terminated at the edge (reverse proxy / ingress), so no HTTPS redirect here.
    app.UseAuthorization();
    app.MapControllers();

    // Liveness: the process is up. Readiness: dependencies (database) are reachable.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    app.MapHealthChecks("/health");

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FlowStockDbContext>().Database.MigrateAsync();
        Log.Information("Database migrations applied on startup");
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "FlowStock API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
