using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ChessAscent.Api.Tests.Features.Accounts;

// The password, its hash, the account's security stamp and the session
// cookie's value must never appear in a response body or a log message. Every
// log level is captured, Trace included, so a message that only shows up when
// someone turns logging up still counts.
public sealed class AccountSecretsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Unusual-passw0rd-for-this-test";

    [Fact]
    public async Task NoResponseBodyOrLogMessageCarriesAnAccountSecret()
    {
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(host => host.ConfigureLogging(logging => logging
            .AddProvider(logs)
            .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = logged.CreateClient();
        var email = Accounts.NewEmail();
        var bodies = new List<string>();

        using var registered = await client.RegisterAsync(email, Password);
        bodies.Add(await ReadBodyAsync(registered));
        var cookieValue = SetCookieHeaderValue.Parse(registered.SessionCookieHeader()).Value.Value;
        foreach (var response in new[]
                 {
                     await client.GetCurrentUserAsync(),
                     await client.SignOutAsync(),
                     await client.SignInAsync(email, Password + "x"),
                     await client.SignInAsync(email, Password),
                     await client.GetCurrentUserAsync(),
                 })
        {
            using (response)
            {
                bodies.Add(await ReadBodyAsync(response));
            }
        }

        var user = await FindUserAsync(logged, email);
        string[] secrets = [Password, user.PasswordHash!, user.SecurityStamp!, cookieValue!];
        secrets.Should().AllSatisfy(secret => secret.Should().NotBeNullOrEmpty());
        logs.Messages.Should().NotBeEmpty();
        foreach (var secret in secrets)
        {
            bodies.Should().NotContain(body => body.Contains(secret, StringComparison.Ordinal));
            logs.Messages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheStoredPasswordIsIdentitysHashNotThePassword()
    {
        using var client = factory.CreateClient();
        var email = Accounts.NewEmail();
        await client.RegisterNewAsync(email);

        var user = await FindUserAsync(factory, email);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        user.PasswordHash.Should().NotBeNullOrEmpty().And.NotContain(Accounts.Password);
        users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, Accounts.Password)
            .Should().Be(PasswordVerificationResult.Success);
    }

    private static async Task<IdentityUser> FindUserAsync(WebApplicationFactory<Program> app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync(email);
        user.Should().NotBeNull();
        return user!;
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    // Keeps every formatted log message, with the exception text when there
    // is one, from every category.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
