using ChessAscent.Api.Features.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Database;

public static class DatabaseServices
{
    // The name the database check reports under in logs.
    private const string HealthCheckName = "database";

    /// <summary>
    /// Registers <see cref="DatabaseOptions"/>, <see cref="AppDbContext"/> on
    /// PostgreSQL, and a readiness check that the database answers.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        // LEARN: AddOptions<T>() starts an options registration. Bind reads the
        // section, ValidateDataAnnotations checks the attributes on the class,
        // and ValidateOnStart runs that check while the application starts, so
        // a missing connection string stops the process at once with a message
        // naming the setting, rather than failing on the first request. Without
        // it, validation runs the first time something reads the options.
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // LEARN: The lambda builds the DbContextOptions, and receives the
        // container so it can read the validated DatabaseOptions. UseNpgsql
        // picks the provider, as a JPA persistence unit names its dialect and
        // data source. The context itself stays Scoped (one per request); the
        // options it is built from never change, so they are a Singleton and
        // the lambda runs once, not once per request.
        services.AddDbContext<AppDbContext>(
            (provider, options) =>
                options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // LEARN: AddDbContextCheck is the counterpart of Spring Boot Actuator's
        // DataSourceHealthIndicator. On each readiness request it creates an
        // AppDbContext in a fresh scope and calls Database.CanConnectAsync(),
        // which opens a connection and reports whether that worked. It does
        // not check that migrations are up to date.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>(HealthCheckName, tags: [HealthEndpoints.ReadinessTag]);

        return services;
    }
}
