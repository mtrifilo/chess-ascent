using System.Net;
using ChessAscent.Api.Features.AppVersion;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChessAscent.Api.Tests.Features.AppVersion;

public sealed class AppVersionEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task VersionReportsTheVersionAndCommitTheApiWasBuiltFrom()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/version", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AppVersionResponse>(
            TestContext.Current.CancellationToken);
        body.Should().Be(AppVersionResponse.FromAssembly(typeof(Program).Assembly));
        body!.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }
}
