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

    private const string GroupPrefix = "/health";
    private const string ReadinessRoute = "/ready";

    /// <summary>The readiness endpoint's path.</summary>
    public const string ReadinessPath = GroupPrefix + ReadinessRoute;

    // LEARN: These are ASP.NET Core's built-in health checks, the counterpart
    // of Spring Boot Actuator's liveness and readiness groups. A health check
    // is a service registered with AddHealthChecks(). Each endpoint runs the
    // checks its predicate selects and answers 503 if any of them is
    // Unhealthy, and 200 if all are Healthy or Degraded. Degraded is a state
    // Actuator does not have: it still answers 200, so it does not take the
    // instance out of service. HealthCheckOptions.ResultStatusCodes holds
    // that mapping.
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // LEARN: MapGroup is the counterpart of a class-level @RequestMapping:
        // every route mapped on the group starts with its prefix, and the
        // group can carry shared settings (authorization, filters) later.
        var health = endpoints.MapGroup(GroupPrefix);

        // Liveness runs no checks: it answers 200 whenever the process can
        // serve a request, so a failing dependency never gets a healthy
        // process restarted.
        health.MapHealthChecks("", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        // Readiness runs every check tagged as a readiness check. With none
        // registered it answers 200.
        health.MapHealthChecks(ReadinessRoute, ReadinessOptions());

        return endpoints;
    }

    /// <summary>
    /// The readiness endpoint's options, a new instance on each call. The
    /// endpoint and its description in the OpenAPI document both read them,
    /// so the status codes the document lists are the ones it answers with.
    /// </summary>
    internal static HealthCheckOptions ReadinessOptions() => new()
    {
        Predicate = check => check.Tags.Contains(ReadinessTag),
    };
}
