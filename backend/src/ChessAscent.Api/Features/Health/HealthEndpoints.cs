using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ChessAscent.Api.Features.Health;

// LEARN: A static class holding extension methods. The `this` before the
// first parameter lets callers write `app.MapHealthEndpoints()` as if the
// method were declared on the endpoint builder. Java has no equivalent; the
// nearest is a static utility method, which callers would have to invoke as
// HealthEndpoints.map(app).
public static class HealthEndpoints
{
    /// <summary>
    /// The tag a health check registers with to be part of readiness, for
    /// example <c>AddHealthChecks().AddCheck(..., tags: [ReadinessTag])</c>.
    /// </summary>
    public const string ReadinessTag = "ready";

    // LEARN: These are ASP.NET Core's built-in health checks, the counterpart
    // of Spring Boot Actuator's liveness and readiness groups. A health check
    // is a service registered with AddHealthChecks(); each endpoint runs the
    // checks its predicate selects and answers 200 when all are healthy and
    // 503 otherwise.
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: it answers 200 whenever the process can
        // serve a request, so a failing dependency never gets a healthy
        // process restarted.
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        // Readiness runs every check tagged as a readiness check. With none
        // registered it answers 200.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadinessTag),
        });

        return endpoints;
    }
}
