using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChessAscent.Api.Engine;

/// <summary>
/// Reports Unhealthy while any configured kind of engine has no working
/// process, and Healthy otherwise, including when no kinds are configured.
/// </summary>
/// <remarks>
/// It reads the pool's state and starts nothing, so it answers at once. The
/// kinds it names appear in the health-check log, not in the response body.
/// </remarks>
internal sealed class EnginePoolReadinessCheck(EnginePool pool) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var waiting = pool.KindsWithoutWorkingEngine();
        return Task.FromResult(waiting.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"No working engine process of kind {string.Join(", ", waiting)}."));
    }
}
