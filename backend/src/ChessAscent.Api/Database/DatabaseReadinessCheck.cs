using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ChessAscent.Api.Database;

// LEARN: IHealthCheck is the counterpart of Spring Boot Actuator's
// HealthIndicator: one method that reports Healthy or Unhealthy. An exception
// thrown from it counts as the registration's failure status (Unhealthy here),
// so a failed connection needs no try/catch. DatabaseServices registers this
// class as a Singleton, so every check shares the one instance and its state.
/// <summary>
/// Reports whether the database accepts a new connection within
/// <see cref="DatabaseServices.ReadinessCheckTimeout"/>.
/// </summary>
/// <remarks>
/// It opens its own unpooled connection rather than going through
/// <see cref="AppDbContext"/>. Against a container that was paused (TCP
/// connections are accepted but the handshake is never answered), a check
/// through a pooled connection took 60 seconds to return after its token was
/// cancelled, and an unpooled open ignored the token until its connect
/// timeout ran out. So the connect timeout is set to the check's, which bounds
/// that case; a host that drops packets, so that the TCP connect itself hangs,
/// is not covered by a test.
/// </remarks>
internal sealed class DatabaseReadinessCheck(DatabaseConnection database) : IHealthCheck
{
    // LEARN: System.Threading.Lock (C# 13, .NET 9) is a dedicated lock
    // object; `lock (_gate)` on it is the counterpart of Java's
    // `synchronized (gate)`. Code inside a lock must not await, which is why
    // the attempt is only started, not awaited, while the lock is held.
    private readonly Lock _gate = new();
    private Task? _attempt;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // LEARN: WaitAsync(token) stops waiting when the token is cancelled
        // without cancelling the task it waits on, so one caller giving up
        // does not fail the attempt the other callers share.
        await CurrentAttempt().WaitAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }

    // Probes that arrive while an attempt is running share it, so however
    // often /health/ready is called, each instance holds at most one
    // connection for it.
    private Task CurrentAttempt()
    {
        lock (_gate)
        {
            if (_attempt is null || _attempt.IsCompleted)
            {
                _attempt = Task.Run(ConnectOnceAsync);
            }

            return _attempt;
        }
    }

    // No cancellation token: the connect timeout in the connection string
    // bounds the attempt, and it belongs to every caller waiting on it.
    private async Task ConnectOnceAsync()
    {
        await using var connection = new NpgsqlConnection(database.ReadinessConnectionString);
        await connection.OpenAsync();
    }
}
