using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace ChessAscent.Api.Tests;

/// <summary>Reads the problem-details bodies every API error carries.</summary>
internal static class ProblemResponses
{
    /// <summary>Reads a problem-details body, checking its media type and that its status matches the response's.</summary>
    public static async Task<T> ReadProblemAsync<T>(this HttpResponseMessage response)
        where T : ProblemDetails
    {
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)response.StatusCode);
        return problem;
    }

    /// <inheritdoc cref="ReadProblemAsync{T}"/>
    public static Task<ProblemDetails> ReadProblemAsync(this HttpResponseMessage response) =>
        response.ReadProblemAsync<ProblemDetails>();

    /// <summary>
    /// The body as JSON text without its trace id, which differs on every
    /// response, so two answers can be compared for everything else.
    /// </summary>
    public static async Task<string> ComparableBodyAsync(this HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }
}
