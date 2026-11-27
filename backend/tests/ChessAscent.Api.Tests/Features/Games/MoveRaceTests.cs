using System.Data.Common;
using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// One move sent twice at once, as a double click or two browser tabs can send
// it, where both requests load the game before either has saved. Sent side by
// side they would usually not overlap: the second would load the game after
// the first had saved, and find the move illegal (see GameEndpointsTests). So
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
        var hold = new HoldTheFirstGameUpdate();
        using var held = factory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold))));
        using var client = held.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateGameAsync(factory);

        var first = client.PostMoveAsync(game.Id, "e2e4");
        await hold.FirstUpdated.WaitAsync(Patience, Cancellation);
        var second = client.PostMoveAsync(game.Id, "e2e4");
        await hold.SecondUpdating.WaitAsync(Patience, Cancellation);
        hold.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondResponse.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.MoveConflictDetail);
        (await client.ReadGameAsync(factory, game.Id)).Moves.Should().Equal(new MoveResponse(1, "e2e4", "e4"));
    }

    // Holds the first update of a game once it has run, which is when its
    // transaction holds the game's row lock, until the test releases it, and
    // reports when a second update is about to run. A hold that is never
    // released ends after Patience, so a broken test fails instead of hanging.
    private sealed class HoldTheFirstGameUpdate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _firstUpdated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondUpdating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _updatesStarted;

        public Task FirstUpdated => _firstUpdated.Task;

        public Task SecondUpdating => _secondUpdating.Task;

        public void Release() => _released.TrySetResult();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsGameUpdate(command) && Interlocked.Increment(ref _updatesStarted) == 2)
            {
                _secondUpdating.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (IsGameUpdate(command) && !_firstUpdated.Task.IsCompleted)
            {
                _firstUpdated.TrySetResult();
                await Task.WhenAny(_released.Task, Task.Delay(Patience, cancellationToken));
            }

            return result;
        }

        private static bool IsGameUpdate(DbCommand command) =>
            command.CommandText.StartsWith("UPDATE \"Games\"", StringComparison.Ordinal);
    }
}
