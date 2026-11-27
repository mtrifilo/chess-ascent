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
        //
        // LEARN: That return type is also what puts this endpoint in the
        // OpenAPI document. Ok<T> implements IEndpointMetadataProvider, an
        // interface with a static abstract method. The framework calls it
        // once, when it builds the endpoint, because the lambda's declared
        // return type implements the interface: no instance and no request
        // are involved. (A Java interface cannot declare a static method that
        // each implementer must supply, so there is no direct counterpart.) It
        // records "200, application/json, AppVersionResponse" as endpoint
        // metadata, and the OpenAPI generator turns that into a response and a
        // schema built from the record, with the /// comments on the record as
        // its descriptions. A lambda returning IResult, or Results.Ok(...),
        // declares nothing, and the document would show no response body.
        // Returning several outcomes is written Results<Ok<T>, NotFound>, a
        // union type the compiler checks, and each case becomes a response.
        version.MapGet("", () => TypedResults.Ok(response));

        return endpoints;
    }
}
