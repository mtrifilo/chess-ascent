using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace ChessAscent.Api.Tests.Features.Accounts;

public sealed class SessionCookieTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ChallengePath = "/test-only/challenge";
    private const string ForbidPath = "/test-only/forbid";

    // Identity's default cookie lifetime; it slides, so it is the lifetime
    // from the latest renewal.
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);

    // Development serves plain HTTP, so the cookie is not marked Secure there.
    [Fact]
    public async Task RegisteringSetsTheSessionCookie()
    {
        using var client = factory.CreateClient();

        using var response = await client.RegisterAsync(Accounts.NewEmail());

        var cookie = ParseSessionCookie(response);
        ShouldBeTheSessionCookie(cookie);
        cookie.Secure.Should().BeFalse();
    }

    [Fact]
    public async Task SigningInSetsTheSessionCookie()
    {
        var email = await factory.RegisterAccountAsync();

        using var client = factory.CreateClient();
        using var response = await client.SignInAsync(email);

        ShouldBeTheSessionCookie(ParseSessionCookie(response));
    }

    [Fact]
    public async Task TheCookieIsSecureOutsideDevelopmentEvenOverPlainHttp()
    {
        using var production = factory.WithWebHostBuilder(host => host.UseEnvironment("Production"));
        using var client = production.CreateClient();

        using var response = await client.RegisterAsync(Accounts.NewEmail());

        ParseSessionCookie(response).Secure.Should().BeTrue();
    }

    [Fact]
    public async Task SigningOutExpiresTheCookie()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var response = await client.SignOutAsync();

        var cookie = ParseSessionCookie(response);
        cookie.Value.Value.Should().BeEmpty();
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    // The endpoints only ever challenge (GET /auth/me), so the forbid answer is
    // checked through a test-only path that asks the cookie handler directly.
    [Theory]
    [InlineData(ChallengePath, HttpStatusCode.Unauthorized)]
    [InlineData(ForbidPath, HttpStatusCode.Forbidden)]
    public async Task TheCookieHandlerAnswersWithAStatusAndProblemDetailsNotARedirect(string path, HttpStatusCode expected)
    {
        using var withPaths = factory.WithWebHostBuilder(host =>
            host.ConfigureServices(services => services.AddTransient<IStartupFilter, RefusingEndpointsStartupFilter>()));
        using var client = withPaths.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        response.Headers.Location.Should().BeNull();
        await response.ReadProblemAsync();
    }

    // Persistent (it has an expiry, so the browser keeps it across restarts),
    // out of reach of the page's script, held back from most cross-site
    // requests, and sent only to the host that set it.
    private static void ShouldBeTheSessionCookie(SetCookieHeaderValue cookie)
    {
        cookie.Expires.Should().BeCloseTo(DateTimeOffset.UtcNow + SessionLifetime, TimeSpan.FromMinutes(1));
        cookie.HttpOnly.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookie.Domain.HasValue.Should().BeFalse();
        cookie.Path.Value.Should().Be("/");
    }

    private static SetCookieHeaderValue ParseSessionCookie(HttpResponseMessage response)
    {
        var header = response.SessionCookieHeader();
        header.Should().NotBeNull();
        return SetCookieHeaderValue.Parse(header);
    }

    // Adds two paths behind the application's own middleware (so behind
    // UseStatusCodePages) that challenge and forbid through the default
    // scheme, which is the Identity cookie.
    private sealed class RefusingEndpointsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ChallengePath, branch => branch.Run(context => context.ChallengeAsync()));
            app.Map(ForbidPath, branch => branch.Run(context => context.ForbidAsync()));
        };
    }
}
