using System.Reflection;
using ChessAscent.Api.Features.Health;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace ChessAscent.Api.ApiDocument;

// LEARN: Microsoft.AspNetCore.OpenApi plays the part springdoc-openapi plays
// in a Spring Boot app: it builds an OpenAPI document from the endpoints the
// application maps. It reads the metadata each endpoint carries: the route,
// the HTTP method, the parameter types, attributes such as [EndpointSummary],
// and the response types. Where springdoc mostly learns a response's shape
// from a controller method's return type plus annotations, a minimal API
// endpoint states it in its handler's declared return type (see
// AppVersionEndpoints for how a TypedResults type becomes a response). The
// front end's TypeScript types are generated from this document, so the C#
// types are the single source of the API contract.
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
            options.KeepNullWhereItBelongs();
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

            // By default Scalar sends telemetry, and offers an AI chat, MCP
            // integration and, on localhost, a developer toolbar whose
            // buttons upload the document to Scalar's servers. Nothing about
            // this project is sent out by default, so all four are off.
            app.MapScalarApiReference(options => options
                .DisableTelemetry()
                .DisableAgent()
                .DisableMcp()
                .HideDeveloperTools());
        }

        return app;
    }
}
