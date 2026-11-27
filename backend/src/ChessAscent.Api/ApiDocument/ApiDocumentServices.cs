using System.Reflection;
using ChessAscent.Api.Features.Health;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace ChessAscent.Api.ApiDocument;

// LEARN: ASP.NET Core describes its own endpoints as an OpenAPI document, so
// there is no springdoc-style library scanning annotations at run time. It
// reads the metadata every endpoint already carries: the route, the HTTP
// method, the parameter types, and what the handler declares it returns (see
// AppVersionEndpoints for how a TypedResults return type becomes a response).
// The front end's TypeScript types are generated from that document, so the
// C# types are the single source of the API contract.
public static class ApiDocumentServices
{
    /// <summary>
    /// True while the build-time document generator, rather than a real
    /// server, is running the application.
    /// </summary>
    /// <remarks>
    /// LEARN: Microsoft.Extensions.ApiDescription.Server generates the document
    /// at build time by running Program.cs itself, inside a tool named
    /// GetDocument.Insider, with a stand-in server that never listens. Every
    /// line of Program.cs runs, so start-up work that needs the outside world
    /// (here, migrating the database) checks this first. Microsoft's
    /// documentation gives this entry-assembly check as the way to tell.
    /// </remarks>
    public static bool IsBuildTimeGeneration { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>
    /// Registers the OpenAPI document, pinned to OpenAPI 3.1 and including
    /// the readiness endpoint.
    /// </summary>
    public static IServiceCollection AddApiDocument(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            // ASP.NET Core 11 writes OpenAPI 3.2 by default, which
            // openapi-typescript does not yet state that it supports. This
            // sets the document served at run time; ChessAscent.Api.csproj
            // pins the build-time one, which does not read it.
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.DescribeReadinessEndpoint();
        });

    /// <summary>
    /// In Development only, serves the document at <c>/openapi/v1.json</c> and
    /// the Scalar API explorer at <c>/scalar</c>.
    /// </summary>
    public static WebApplication MapApiDocumentInDevelopment(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            // Scalar's telemetry and its AI chat both reach Scalar's own
            // servers; nothing about this project is sent out by default.
            app.MapScalarApiReference(options => options.DisableTelemetry().DisableAgent());
        }

        return app;
    }
}
