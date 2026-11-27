using System.Net;
using ChessAscent.Api.Security;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ChessAscent.Api.Tests.Security;

// Each forged request below is what a browser sends when a page on another
// site submits a hidden form to the API while the player is signed in: the
// player's cookie (the HttpClient keeps it), a form body, and the headers
// the browser adds, which the attacking page cannot change.
public sealed class CrossSiteRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AttackerOrigin = "https://attacker.example";

    // The web app's development address; the test server's own is http://localhost.
    private const string WebAppOrigin = "http://localhost:3000";

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
        (await response.ReadProblemAsync()).Detail.Should().Be(CrossSiteRequests.RefusedDetail);
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

    // What the front end's own requests look like after the development
    // rewrite: the browser saw one origin, so it says same-origin, but the
    // Origin is the web app's and the Host the API's.
    [Fact]
    public async Task AFrontEndRequestThroughTheRewritePasses()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var request = Post("/auth/sign-out", "same-origin", WebAppOrigin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // A page may set Accept on its forged request. One that rules out JSON
    // still gets a 403, as plain text, and never a server error.
    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("image/png")]
    public async Task ARefusalThatCannotBeJsonIsStillA403(string accept)
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var forged = Post("/auth/sign-out", "cross-site", AttackerOrigin, EmptyForm());
        forged.Headers.Accept.ParseAdd(accept);
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    // The refusal logs the headers it was given, and any client can choose
    // them: a host that Uri accepts but cannot turn into ASCII, a terminal
    // escape sequence, and quotes that would imitate the log's own. Each is
    // still refused with a 403, and logged exactly as sent, escaped.
    [Theory]
    [InlineData("http://canada.c\u2100.microsoft.com", "http://canada.c\\u2100.microsoft.com")]
    [InlineData("https://attacker.example\u001b[2K\u001b[1A", "https://attacker.example\\u001b[2K\\u001b[1A")]
    [InlineData("x', Origin 'https://app.example", "x\\u0027, Origin \\u0027https://app.example")]
    [InlineData("a\\u001b", "a\\u005cu001b")]
    public async Task ARefusalOfAnyOriginIsA403AndLogsItEscaped(string origin, string logged)
    {
        var refusal = await RefuseAndReadLogAsync(origin);

        refusal.Should().Be($"Refused POST /auth/sign-out: Sec-Fetch-Site 'cross-site', Origin '{logged}'.");
    }

    // Kept to its first 100 characters, each of which here becomes six.
    [Fact]
    public async Task ARefusalLogsOnlyTheStartOfALongOrigin()
    {
        var refusal = await RefuseAndReadLogAsync(new string('\u00e9', 150));

        refusal.Should().Be(
            "Refused POST /auth/sign-out: Sec-Fetch-Site 'cross-site', Origin '"
            + string.Concat(Enumerable.Repeat("\\u00e9", 100)) + "...'.");
    }

    private async Task<string> RefuseAndReadLogAsync(string origin)
    {
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(host => host.ConfigureLogging(logging => logging
            .AddProvider(logs)
            .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = logged.CreateClient();

        using var forged = new HttpRequestMessage(HttpMethod.Post, "/auth/sign-out") { Content = EmptyForm() };
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        forged.Headers.TryAddWithoutValidation(HeaderNames.Origin, origin).Should().BeTrue();
        using var response = await client.SendAsync(forged, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var refusal = logs.Messages.Should()
            .ContainSingle(message => message.StartsWith("Refused POST /auth/sign-out", StringComparison.Ordinal)).Subject;
        refusal.Should().MatchRegex(@"^[ -~]*\z");
        return refusal;
    }

    // A browser without Fetch Metadata, behind the same rewrite, has only an
    // Origin that differs from the API's Host, so it is refused.
    [Fact]
    public async Task AnOlderBrowserThroughTheRewriteIsRefused()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var request = Post("/auth/sign-out", secFetchSite: null, WebAppOrigin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
