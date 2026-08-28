using System.Text;
using System.Text.Json.Serialization;
using FlowStock.Api;
using FlowStock.Api.Authorization;
using FlowStock.Api.BackgroundJobs;
using FlowStock.Api.Middleware;
using FlowStock.Application;
using FlowStock.Application.Common;
using FlowStock.Infrastructure;
using FlowStock.Infrastructure.Identity;
using FlowStock.Infrastructure.Persistence;
using FlowStock.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // writeToProviders keeps any other registered logging provider in the loop. There are none in
    // production; it is what lets an integration test see what the API logged when a request fails.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(), writeToProviders: true);

    builder.Services
        .AddControllers(options => options.Filters.Add<ValidationFilter>())
        // Enums travel as their names, so the wire contract stays readable and stable
        // even if an enum is ever reordered.
        .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = ModelBindingErrors.ToErrorResponse);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new() { Title = "FlowStock API", Version = "v1" });

        // Quantities are decimal. Swashbuckle would otherwise document them as "double", telling
        // clients to use exactly the type inventory quantities must never touch.
        options.MapType<decimal>(() => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal" });
        options.MapType<decimal?>(() => new OpenApiSchema
        {
            Type = JsonSchemaType.Number | JsonSchemaType.Null, Format = "decimal"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the token returned by /api/auth/login."
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
        });
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    string.IsNullOrEmpty(jwtOptions.Key) ? new string('x', 32) : jwtOptions.Key)),
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorizationBuilder().AddFlowStockPolicies();

    // Expired lots and unfeedable runs are conditions of time and stock, so something has to look
    // for them. Turned off in tests, which run the same scan deliberately.
    builder.Services.AddHostedService<NotificationScanService>();

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
    app.UseAuthentication();
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

    // Seed users only in Development — seed credentials must never exist in production.
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (seedOptions.Users.Count > 0)
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(seedOptions.Users);
        }
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
