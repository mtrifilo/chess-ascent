using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// One move sent twice at once, as a double click or two browser tabs can send
// it, where both requests load the game before either has saved. Sent side by
// side they would usually not overlap: the second would load the game after
// the first had saved, and find its half-move number taken before playing it
// (see GameEndpointsTests). So
// the first request is held just after its update has locked the game's row,
// and let go only once the second has loaded the game, played the move on
// it, and begun its own update.
public sealed class MoveRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OfTwoRequestsThatPlayTheSameMoveOnTheSamePositionOneIsSavedAndTheOtherRefusedWith409()
    {
        var hold = HoldTheFirstCommand.GameUpdates();
        using var held = factory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold))));
        using var client = held.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateGameAsync();

        var first = client.PostMoveAsync(game.Id, "e2e4", 1);
        await hold.FirstRan.WaitAsync(Patience, Cancellation);
        var second = client.PostMoveAsync(game.Id, "e2e4", 1);
        await hold.SecondStarting.WaitAsync(Patience, Cancellation);
        hold.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondResponse.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.MoveConflictDetail);
        (await client.ReadGameAsync(game.Id)).Moves.Should().Equal(new MoveResponse(1, "e2e4", "e4"));
    }
}
