using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ChessAscent.Api.Database;

// LEARN: IHealthCheck is the counterpart of Spring Boot Actuator's
// HealthIndicator: one method that reports Healthy or Unhealthy. An exception
// thrown from it counts as the registration's failure status (Unhealthy here),
// so a failed connection needs no try/catch. The class is registered with
// AddCheck<T> in DatabaseServices and created from the DI container for each
// check, so its constructor can ask for services.
/// <summary>
/// Reports whether the database accepts a new connection within
/// <see cref="DatabaseServices.ReadinessCheckTimeout"/>.
/// </summary>
/// <remarks>
/// It opens its own connection rather than going through
/// <see cref="AppDbContext"/>. A pooled connection to a server that has
/// stopped answering can take a minute to give up even after the check is
/// cancelled, and Npgsql does not cancel a connection attempt part-way through
/// the handshake. Its connect timeout does bound the attempt, so the check
/// uses an unpooled connection with that timeout set to the check's.
/// </remarks>
internal sealed class DatabaseReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    private static readonly int TimeoutSeconds = (int)Math.Ceiling(DatabaseServices.ReadinessCheckTimeout.TotalSeconds);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(DatabaseServices.ReadConnectionString(configuration))
        {
            Pooling = false,
            Timeout = TimeoutSeconds,
        };

        await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
