using ChessAscent.Api.Features.Health;
using Microsoft.EntityFrameworkCore;

namespace ChessAscent.Api.Database;

public static class DatabaseServices
{
    /// <summary>
    /// The name of the API's connection string: the key under the
    /// <c>ConnectionStrings</c> section of configuration.
    /// </summary>
    public const string ConnectionStringName = "ChessAscent";

    /// <summary>
    /// How long the readiness check waits for the database before it reports
    /// Unhealthy, so <c>/health/ready</c> answers 503 promptly when the
    /// database host stops answering rather than when Npgsql's 15-second
    /// connect timeout runs out.
    /// </summary>
    public static readonly TimeSpan ReadinessCheckTimeout = TimeSpan.FromSeconds(3);

    // The name the database check reports under in logs.
    private const string HealthCheckName = "database";

    /// <summary>
    /// Registers <see cref="AppDbContext"/> on PostgreSQL, and a readiness
    /// check that the database answers.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        // LEARN: The lambda builds the DbContextOptions, and receives the
        // container so it can read configuration. UseNpgsql picks the
        // provider, as a JPA persistence unit names its dialect and data
        // source. The context itself stays Scoped (one per request); the
        // options it is built from never change, so they are a Singleton and
        // the lambda runs once, the first time a context is created.
        services.AddDbContext<AppDbContext>(
            (provider, options) =>
                options.UseNpgsql(ReadConnectionString(provider.GetRequiredService<IConfiguration>())),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // LEARN: A health check has no time limit unless its registration sets
        // one. With a timeout, the check's CancellationToken is cancelled when
        // it passes, and a check that then throws counts as Unhealthy. The
        // check itself also bounds its connection attempt by the same value
        // (see DatabaseReadinessCheck), because the token alone does not stop
        // Npgsql in every case. It does not check that migrations are up to
        // date.
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>(
                HealthCheckName,
                tags: [HealthEndpoints.ReadinessTag],
                timeout: ReadinessCheckTimeout);

        return services;
    }

    // LEARN: ConnectionStrings is a configuration section that ASP.NET Core
    // gives a helper of its own: GetConnectionString("ChessAscent") reads the
    // key ConnectionStrings:ChessAscent, the counterpart of Spring Boot's
    // spring.datasource.url. Like every other key, it comes from the
    // configuration providers that Program.cs describes, and a later provider
    // wins. Locally, appsettings.Development.json holds the string for the
    // Docker Compose database. Anywhere else, set the environment variable
    // ConnectionStrings__ChessAscent: a double underscore stands for the colon
    // between section and key, the way Spring's relaxed binding reads
    // SPRING_DATASOURCE_URL.
    //
    // The start-up migration creates the first AppDbContext, so a missing
    // connection string stops the process there, before the API serves.
    internal static string ReadConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string. Set ConnectionStrings:{ConnectionStringName} in configuration, "
                + $"for example with the environment variable ConnectionStrings__{ConnectionStringName}.");
        }

        return connectionString;
    }
}
