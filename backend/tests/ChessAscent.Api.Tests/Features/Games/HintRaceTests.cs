using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.Extensions.DependencyInjection;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// Two requests for a hint in the same position at once, as a double click or
// two browser tabs can send them, where both have found the hint before
// either has counted it. Sent side by side they would usually not overlap,
// so the first request is held just after its update has counted the hint
// and locked the game's row, and let go only once the second has loaded the
// game, been given the hint, and begun its own update.
public sealed class HintRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoRequestsForAHintInTheSamePositionAtOnceBothGetItAndItIsCountedOnce()
    {
        var hold = new HoldTheFirstCommand(command =>
            HoldTheFirstCommand.IsGameUpdate(command) && command.CommandText.Contains("\"HintCount\"", StringComparison.Ordinal));
        var hints = FakeHintProvider.Hinting("e2e4");
        using var held = factory.WithFakes(
            new FakeOpponent(),
            hints,
            configure: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold)));
        using var client = held.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateComputerGameAsync("white");

        var first = client.PostHintAsync(game.Id, 1);
        await hold.FirstRan.WaitAsync(Patience, Cancellation);
        var second = client.PostHintAsync(game.Id, 1);
        await hold.SecondStarting.WaitAsync(Patience, Cancellation);
        hold.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondResponse.ReadHintAsync()).Should().Be(await firstResponse.ReadHintAsync());
        hints.Calls.Should().HaveCount(2);
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(1);
    }
}
