using System.Data.Common;
using System.Net;
using ChessAscent.Api.Database;
using ChessAscent.Api.Hints;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var hold = new HoldTheFirstHintCount();
        var hints = FakeHintProvider.Hinting("e2e4");
        using var held = factory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(hold));
            services.RemoveAll<IOpponent>();
            services.AddSingleton<IOpponent>(new FakeOpponent());
            services.RemoveAll<IHintProvider>();
            services.AddSingleton<IHintProvider>(hints);
        }));
        using var client = held.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateComputerGameAsync("white");

        var first = client.PostHintAsync(game.Id, 1);
        await hold.FirstCounted.WaitAsync(Patience, Cancellation);
        var second = client.PostHintAsync(game.Id, 1);
        await hold.SecondCounting.WaitAsync(Patience, Cancellation);
        hold.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondResponse.ReadHintAsync()).Should().Be(await firstResponse.ReadHintAsync());
        hints.Calls.Should().HaveCount(2);
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(1);
    }

    // Holds the first hint count once it has run, which is when its
    // transaction holds the game's row lock, until the test releases it, and
    // reports when a second is about to run. A hold that is never released
    // ends after Patience, so a broken test fails instead of hanging.
    private sealed class HoldTheFirstHintCount : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _firstCounted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondCounting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _countsStarted;

        public Task FirstCounted => _firstCounted.Task;

        public Task SecondCounting => _secondCounting.Task;

        public void Release() => _released.TrySetResult();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (IsHintCount(command) && Interlocked.Increment(ref _countsStarted) == 2)
            {
                _secondCounting.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (IsHintCount(command) && !_firstCounted.Task.IsCompleted)
            {
                _firstCounted.TrySetResult();
                await Task.WhenAny(_released.Task, Task.Delay(Patience, cancellationToken));
            }

            return result;
        }

        private static bool IsHintCount(DbCommand command) =>
            command.CommandText.StartsWith("UPDATE \"Games\"", StringComparison.Ordinal)
            && command.CommandText.Contains("\"HintCount\"", StringComparison.Ordinal);
    }
}
