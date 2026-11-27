using System.Data.Common;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChessAscent.Api.Tests.Features.Games;

// Two appends to one game at the same half-move number, as a double-clicked
// move or two browser tabs send them. Sent side by side they would usually
// not overlap: one would commit before the other began. So each runs on a
// context of its own whose transactions are held until both have begun,
// which puts both inside their transactions before either has written.
public sealed class AppendRaceTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoAppendsAtOneHalfMoveAtOnceSaveOneAndAnswerTheOtherWithAConflict()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));
        var board = GameData.Rules.NewGame();
        var (opening, afterOpening) = board.Play("f2f3", "e7e5", "g2g4");
        await factory.WithStoreAsync(store => store.AppendMovesAsync(owner, game.Id, 1, opening, afterOpening, Cancellation));

        // Black to move. One request mates; the other plays a quiet move and
        // a reply, so the two differ in move count, moves and status, and a
        // mix of the two would show.
        var (mate, afterMate) = board.Branch().Play("d8h4");
        var (quiet, afterQuiet) = board.Branch().Play("a7a6", "a2a3");
        var transactions = new TransactionsHeldUntilBothBegin();
        await using var firstContext = NewContext(transactions);
        await using var secondContext = NewContext(transactions);
        var first = new GameStore(firstContext, TimeProvider.System);
        var second = new GameStore(secondContext, TimeProvider.System);

        var outcomes = await Task.WhenAll(
            Task.Run(() => first.AppendMovesAsync(owner, game.Id, 4, mate, afterMate, Cancellation), Cancellation),
            Task.Run(() => second.AppendMovesAsync(owner, game.Id, 4, quiet, afterQuiet, Cancellation), Cancellation));
        var saved = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        transactions.BothBegan.Should().BeTrue("the two appends must have raced");
        outcomes.Select(outcome => outcome.Value?.GetType())
            .Should().BeEquivalentTo([typeof(MovesAppended), typeof(HalfMoveConflict)]);
        outcomes.Should().ContainSingle(outcome => outcome.Value is HalfMoveConflict)
            .Which.Value.Should().Be(new HalfMoveConflict(4));

        var mateWon = outcomes[0].Value is MovesAppended;
        var (winningMoves, winningStatus) = mateWon ? (mate, afterMate) : (quiet, afterQuiet);
        saved!.Moves.Should().Equal([.. opening, .. winningMoves]);
        saved.Rebuild().Status.Should().Be(winningStatus);
        saved.Status.Should().Be(mateWon ? GameProgress.Finished : GameProgress.Ongoing);
        saved.Result.Should().Be(mateWon ? GameResult.BlackWins : null);
        saved.EndReason.Should().Be(mateWon ? GameEndReason.Checkmate : null);
        var winnersTime = outcomes.Select(outcome => outcome.Value).OfType<MovesAppended>().Single().UpdatedAt;
        saved.UpdatedAt.Should().Be(winnersTime);
    }

    private AppDbContext NewContext(TransactionsHeldUntilBothBegin transactions) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(transactions)
            .Options);

    // Holds the first transaction to begin until the second has begun too. A
    // transaction that waits more than ten seconds goes ahead, so an append
    // that never begins fails the test instead of hanging it.
    private sealed class TransactionsHeldUntilBothBegin : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _bothBegan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _begun;

        public bool BothBegan => _bothBegan.Task.IsCompletedSuccessfully;

        public override async ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            var begun = Interlocked.Increment(ref _begun);
            if (begun == 2)
            {
                _bothBegan.TrySetResult();
            }
            else if (begun == 1)
            {
                await Task.WhenAny(_bothBegan.Task, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            }

            return result;
        }
    }
}
