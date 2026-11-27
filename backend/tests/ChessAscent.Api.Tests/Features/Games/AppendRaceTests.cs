using System.Data.Common;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ChessAscent.Api.Tests.Features.Games;

// Two appends to one game at the same half-move number, as a double-clicked
// move or two browser tabs send them. Sent side by side they would usually
// not overlap: one would commit before the other began. So the first is held
// just after its update has locked the game's row, the second is started,
// and the test waits until PostgreSQL reports the second waiting for that
// lock before it lets the first commit. Each runs on a context of its own,
// built here so that only the first one's commands are held.
public sealed class AppendRaceTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PatienceForTheLockWait = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Black to move after f3 e5 g4. The first request either mates or plays
    // a quiet move and a reply; the second plays the other line. The two
    // differ in move count, moves and status, so a mix of the two would show.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAppendThatWaitsOnAnotherForTheSameHalfMoveIsRefusedAndSavesNothing(bool firstMates)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var board = GameData.Rules.NewGame();
        var (opening, afterOpening) = board.Play("f2f3", "e7e5", "g2g4");
        await factory.AppendAsync(owner, game.Id, 1, opening, afterOpening);
        var mate = board.Branch().Play("d8h4");
        var quiet = board.Branch().Play("a7a6", "a2a3");
        var (firstMoves, firstStatus) = firstMates ? mate : quiet;
        var (secondMoves, secondStatus) = firstMates ? quiet : mate;

        var hold = new HoldAfterTheGameUpdate();
        await using var firstContext = NewContext(hold);
        await using var secondContext = NewContext();
        await secondContext.Database.OpenConnectionAsync(Cancellation);
        var secondBackend = ((NpgsqlConnection)secondContext.Database.GetDbConnection()).ProcessID;
        var first = new GameStore(firstContext, TimeProvider.System);
        var second = new GameStore(secondContext, TimeProvider.System);

        var firstAppend = Task.Run(() => first.AppendMovesAsync(owner, game.Id, 4, firstMoves, firstStatus, Cancellation), Cancellation);
        await hold.Updated.WaitAsync(PatienceForTheLockWait, Cancellation);
        var secondAppend = Task.Run(() => second.AppendMovesAsync(owner, game.Id, 4, secondMoves, secondStatus, Cancellation), Cancellation);
        var secondWaitedOnTheLock = await WaitsOnALockAsync(secondBackend);
        hold.Release();
        var firstOutcome = await firstAppend;
        var secondOutcome = await secondAppend;
        var saved = await factory.LoadGameAsync(owner, game.Id);

        secondWaitedOnTheLock.Should().BeTrue("the second append must have waited for the first one's row lock");
        var appended = firstOutcome.Value.Should().BeOfType<MovesAppended>().Subject;
        secondOutcome.Value.Should().Be(firstMates ? (object)new GameFinished() : new HalfMoveConflict());
        saved!.Moves.Should().Equal([.. opening, .. firstMoves]);
        saved.Rebuild().Status.Should().Be(firstStatus);
        saved.Status.Should().Be(firstMates ? GameProgress.Finished : GameProgress.Ongoing);
        saved.Result.Should().Be(firstMates ? GameResult.BlackWins : null);
        saved.EndReason.Should().Be(firstMates ? GameEndReason.Checkmate : null);
        saved.UpdatedAt.Should().Be(appended.UpdatedAt);
    }

    private AppDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(interceptors)
            .Options);

    // Asks PostgreSQL, from a connection of its own, whether the backend is
    // blocked waiting for a lock, polling until it is or patience runs out.
    private async Task<bool> WaitsOnALockAsync(int backend)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid", connection);
        command.Parameters.AddWithValue("pid", backend);

        var giveUpAt = DateTime.UtcNow + PatienceForTheLockWait;
        while (DateTime.UtcNow < giveUpAt)
        {
            if (await command.ExecuteScalarAsync(Cancellation) is "Lock")
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation);
        }

        return false;
    }

    // Holds the append once its UPDATE of the game has run, which is when it
    // holds the game's row lock, until the test releases it. A hold that is
    // never released ends after ten seconds, so a broken test fails instead
    // of hanging.
    private sealed class HoldAfterTheGameUpdate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _updated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Updated => _updated.Task;

        public void Release() => _released.TrySetResult();

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Games\"", StringComparison.Ordinal))
            {
                _updated.TrySetResult();
                await Task.WhenAny(_released.Task, Task.Delay(PatienceForTheLockWait, cancellationToken));
            }

            return result;
        }
    }
}
