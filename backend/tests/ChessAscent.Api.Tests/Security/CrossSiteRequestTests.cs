using System.Net;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.Net.Http.Headers;

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

        using var forged = Post("/auth/sign-out", secFetchSite, AttackerOrigin, EmptyForm());
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

        using var forged = Post("/auth/sign-out", secFetchSite: null, AttackerOrigin, EmptyForm());
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetSignedInEmailAsync()).Should().Be(email);
    }

    // Signing a victim in to the attacker's account is a forgery too.
    [Fact]
    public async Task ASignInStartedByAnotherSiteIsRefused()
    {
        var email = await factory.RegisterAccountAsync();

        using var client = factory.CreateClient();
        using var forged = Post(
            "/auth/sign-in", "cross-site", AttackerOrigin, JsonContent.Create(new { email, password = Accounts.Password }));
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.SessionCookieHeader().Should().BeNull();
    }

    [Fact]
    public async Task ASameOriginRequestPasses()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var request = Post("/auth/sign-out", "same-origin", "http://localhost");
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

        using var request = WithBrowserHeaders(
            new HttpRequestMessage(HttpMethod.Get, "/auth/me"), "cross-site", AttackerOrigin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Post(string path, string? secFetchSite, string origin, HttpContent? content = null) =>
        WithBrowserHeaders(new HttpRequestMessage(HttpMethod.Post, path) { Content = content }, secFetchSite, origin);

    // The headers a browser adds to a request, which the page that started
    // it cannot set. No header constant exists for Sec-Fetch-Site.
    private static HttpRequestMessage WithBrowserHeaders(HttpRequestMessage request, string? secFetchSite, string origin)
    {
        if (secFetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", secFetchSite);
        }

        request.Headers.Add(HeaderNames.Origin, origin);
        return request;
    }

    private static FormUrlEncodedContent EmptyForm() => new([]);
}
