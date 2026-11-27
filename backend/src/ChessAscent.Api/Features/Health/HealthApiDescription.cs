using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

namespace ChessAscent.Api.Features.Health;

// LEARN: MapHealthChecks maps a plain request handler with no declared
// parameters or return type, so the OpenAPI generator has nothing to describe
// and leaves the health endpoints out of the document. A document transformer
// is a callback that edits the finished document, the counterpart of a
// springdoc OpenApiCustomizer bean; this one adds the readiness endpoint by
// hand, so the front end gets types for it too.
internal static class HealthApiDescription
{
    // The built-in health check response writer answers with the overall
    // status's name, such as "Healthy", as plain text.
    private const string BodyMediaType = "text/plain";

    /// <summary>
    /// Adds <c>GET /health/ready</c> to the document: one response per status
    /// code the endpoint answers with, each listing the bodies it can carry.
    /// </summary>
    public static OpenApiOptions DescribeReadinessEndpoint(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            var responses = new OpenApiResponses();
            foreach (var group in HealthEndpoints.ReadinessOptions().ResultStatusCodes
                         .GroupBy(mapping => mapping.Value, mapping => mapping.Key)
                         .OrderBy(group => group.Key))
            {
                responses[group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    StatusResponse([.. group]);
            }

            document.Paths ??= [];
            document.Paths[HealthEndpoints.ReadinessPath] = new OpenApiPathItem
            {
                Operations = new()
                {
                    [HttpMethod.Get] = new OpenApiOperation
                    {
                        Summary = "Whether the API is ready to serve: every readiness check passes.",
                        Responses = responses,
                    },
                },
            };

            return Task.CompletedTask;
        });

    private static OpenApiResponse StatusResponse(IReadOnlyList<HealthStatus> statuses) => new()
    {
        Description = $"The overall status is {string.Join(" or ", statuses)}.",
        Content = new Dictionary<string, IOpenApiMediaType>
        {
            [BodyMediaType] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [.. statuses.Select(status => JsonValue.Create(status.ToString()))],
                },
            },
        },
    };
}
