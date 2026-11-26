namespace ChessAscent.Api.Features.AppVersion;

public static class AppVersionEndpoints
{
    public static IEndpointRouteBuilder MapAppVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The assembly's attributes cannot change while the process runs, so
        // they are read once here rather than on every request.
        var response = AppVersionResponse.FromAssembly(typeof(AppVersionEndpoints).Assembly);

        var version = endpoints.MapGroup("/version");

        // LEARN: A Minimal API endpoint is a route plus a lambda; there is no
        // controller class and no @GetMapping. TypedResults.Ok(...) returns an
        // Ok<AppVersionResponse>, a value that knows its own status code and
        // body type, unlike a Spring ResponseEntity<?> whose type says nothing
        // about the status. The framework serializes the record to JSON with
        // camelCase property names.
        version.MapGet("", () => TypedResults.Ok(response));

        return endpoints;
    }
}
