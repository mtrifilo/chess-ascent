using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ChessAscent.Api.Tests.Features.Accounts;

// Two registrations for one email that arrive together, as a double-clicked
// sign-up button sends them. Sent side by side they would usually not race
// at all: one would insert its row before the other checked for it. So the
// application here holds every save until two have arrived, which puts both
// requests past Identity's check for an existing account before either
// inserts, and leaves the database's unique index to settle it.
public sealed class RegistrationRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task TwoRegistrationsForOneEmailAtOnceGiveOneAccountAndOneDuplicateEmailAnswer()
    {
        var saves = new SavesHeldUntilBothArrive();
        using var racing = factory.WithWebHostBuilder(host => host.ConfigureServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(saves))));
        using var first = racing.CreateClient();
        using var second = racing.CreateClient();
        var email = Accounts.NewEmail();

        var responses = await Task.WhenAll(first.RegisterAsync(email), second.RegisterAsync(email.ToUpperInvariant()));

        saves.BothArrived.Should().BeTrue("the two registrations must have raced");
        responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var duplicate = await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict).ReadProblemAsync();
        duplicate.Detail.Should().Be(AccountEndpoints.DuplicateEmailDetail);
        responses.Single(response => response.StatusCode == HttpStatusCode.Conflict)
            .SessionCookieHeader().Should().BeNull();
        (await CountAccountsAsync(email)).Should().Be(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private async Task<int> CountAccountsAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var normalized = users.NormalizeEmail(email);
        return await users.Users.CountAsync(user => user.NormalizedEmail == normalized, TestContext.Current.CancellationToken);
    }

    // Holds each of the first two saves until the second arrives.
    private sealed class SavesHeldUntilBothArrive : SaveChangesInterceptor
    {
        private readonly TwoArrivals _arrivals = new();

        public bool BothArrived => _arrivals.BothArrived;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await _arrivals.ArriveAsync(cancellationToken);
            return result;
        }
    }
}
