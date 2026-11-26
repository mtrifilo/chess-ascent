using ChessAscent.Api.Features.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Database;

public static class DatabaseServices
{
    /// <summary>The name the database check reports under in health results.</summary>
    public const string HealthCheckName = "database";

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

        // LEARN: The lambda runs each time a new AppDbContext is created, that
        // is once per scope, and receives the container so it can read the
        // validated options. UseNpgsql picks the provider, as a JPA
        // persistence unit names its dialect and data source.
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

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
