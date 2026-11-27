using System.Net;
using ChessAscent.Api.Features.Accounts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Net.Http.Headers;

namespace ChessAscent.Api.Tests.Features.Accounts;

/// <summary>
/// Calls to the account endpoints, and the test data they use.
/// </summary>
/// <remarks>
/// Every test class shares one database and they run in parallel, so each
/// test registers accounts under emails of its own from <see cref="NewEmail"/>
/// and never assumes what else the users table holds.
/// </remarks>
internal static class Accounts
{
    /// <summary>A password that meets Identity's default rules. Test data only.</summary>
    public const string Password = "Correct-horse-7";

    /// <summary>An email address no other test uses.</summary>
    public static string NewEmail() => $"player-{Guid.NewGuid():N}@example.com";

    public static Task<HttpResponseMessage> RegisterAsync(this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, password), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/auth/sign-in", new SignInRequest(email, password), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> SignOutAsync(this HttpClient client) =>
        client.PostAsync("/auth/sign-out", content: null, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> GetCurrentUserAsync(this HttpClient client) =>
        client.GetAsync("/auth/me", TestContext.Current.CancellationToken);

    /// <summary>Registers a new account and checks that it succeeded.</summary>
    public static async Task RegisterNewAsync(this HttpClient client, string email)
    {
        using var response = await client.RegisterAsync(email);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Registers a new account from a browser of its own, which is then
    /// closed, so the caller's client is not signed in. Returns its email.
    /// </summary>
    public static async Task<string> RegisterAccountAsync(this ApiFactory factory)
    {
        var email = NewEmail();
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(email);
        return email;
    }

    /// <summary>Reads the email that <c>GET /auth/me</c> reports, checking that it answered 200.</summary>
    public static async Task<string> GetSignedInEmailAsync(this HttpClient client)
    {
        using var response = await client.GetCurrentUserAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        return body!.Email;
    }

    /// <summary>The session cookie's Set-Cookie header on a response, if it set one.</summary>
    public static string? SessionCookieHeader(this HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? values.SingleOrDefault(value => value.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
            : null;

    /// <summary>
    /// The session cookie's name: Identity's default, the cookie prefix
    /// followed by the scheme name.
    /// </summary>
    public static readonly string SessionCookieName = CookieAuthenticationDefaults.CookiePrefix + IdentityConstants.ApplicationScheme;
}
