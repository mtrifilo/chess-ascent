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
}
