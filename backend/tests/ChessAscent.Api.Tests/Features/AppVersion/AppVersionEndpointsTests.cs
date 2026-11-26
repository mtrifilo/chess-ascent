using System.Net;
using ChessAscent.Api.Features.AppVersion;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChessAscent.Api.Tests.Features.AppVersion;

public sealed class AppVersionEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task VersionReportsTheApplicationVersion()
    {
        var body = await GetVersionAsync();

        body.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$");
    }

    [Fact]
    public async Task VersionReportsTheCommitWhenBuiltFromAGitCheckout()
    {
        // A build from a source archive has no commit to report.
        Assert.SkipUnless(IsInsideGitCheckout(AppContext.BaseDirectory), "Not built from a git checkout.");

        var body = await GetVersionAsync();

        body.Commit.Should().MatchRegex("^[0-9a-f]{40}$");
    }

    private async Task<AppVersionResponse> GetVersionAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/version", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AppVersionResponse>(
            TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        return body!;
    }

    // A worktree has a .git file rather than a .git directory, so both count.
    private static bool IsInsideGitCheckout(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return true;
            }
        }

        return false;
    }
}
