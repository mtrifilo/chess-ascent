using System.Net;
using ChessAscent.Api.Tests.Features.Accounts;

namespace ChessAscent.Api.Tests.Security;

// Each forged request below is what a browser sends when a page on another
// site submits a hidden form to the API while the player is signed in: the
// player's cookie (the HttpClient keeps it), a form body, and the headers
// the browser adds, which the attacking page cannot change.
public sealed class CrossSiteRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AttackerOrigin = "https://attacker.example";

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task AFormPostStartedByAnotherSiteIsRefusedAndLeavesTheSessionAlone(string secFetchSite)
    {
        using var client = factory.CreateClient();
        var email = Accounts.NewEmail();
        await client.RegisterNewAsync(email);

        using var forged = ForgedFormPost("/auth/sign-out", secFetchSite, AttackerOrigin);
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await response.ReadProblemAsync();
        response.SessionCookieHeader().Should().BeNull();
        (await client.GetSignedInEmailAsync()).Should().Be(email);
    }

    // A browser from before Fetch Metadata sends Origin but no Sec-Fetch-Site.
    [Fact]
    public async Task AFormPostFromAnotherOriginWithoutFetchMetadataIsRefused()
    {
        using var client = factory.CreateClient();
        var email = Accounts.NewEmail();
        await client.RegisterNewAsync(email);

        using var forged = ForgedFormPost("/auth/sign-out", secFetchSite: null, AttackerOrigin);
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetSignedInEmailAsync()).Should().Be(email);
    }

    // Signing a victim in to the attacker's account is a forgery too.
    [Fact]
    public async Task ASignInStartedByAnotherSiteIsRefused()
    {
        var email = Accounts.NewEmail();
        using (var registering = factory.CreateClient())
        {
            await registering.RegisterNewAsync(email);
        }

        using var client = factory.CreateClient();
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-in")
        {
            Content = JsonContent.Create(new { email, password = Accounts.Password }),
        };
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        forged.Headers.Add("Origin", AttackerOrigin);
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.SessionCookieHeader().Should().BeNull();
    }

    [Fact]
    public async Task ASameOriginRequestPasses()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-out");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Reading is not refused: a cross-site page cannot read the response, and
    // a GET changes nothing.
    [Fact]
    public async Task ACrossSiteGetPasses()
    {
        using var client = factory.CreateClient();
        var email = Accounts.NewEmail();
        await client.RegisterNewAsync(email);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage ForgedFormPost(string path, string? secFetchSite, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent([]),
        };
        if (secFetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", secFetchSite);
        }

        request.Headers.Add("Origin", origin);
        return request;
    }
}
