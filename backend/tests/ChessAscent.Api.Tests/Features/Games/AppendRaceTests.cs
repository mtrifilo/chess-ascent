using System.Data.Common;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChessAscent.Api.Tests.Features.Games;

// Two appends to one game at the same half-move number, as a double-clicked
// move or two browser tabs send them. Sent side by side they would usually
// not overlap: one would commit before the other began. So each runs on a
// context of its own whose transactions are held until both have begun,
// which puts both inside their transactions before either has written. The
// two contexts are built here rather than taken from an application, so the
// hold sees these two transactions and no others.
public sealed class AppendRaceTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoAppendsAtOneHalfMoveAtOnceSaveOneAndAnswerTheOtherWithAConflict()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var board = GameData.Rules.NewGame();
        var (opening, afterOpening) = board.Play("f2f3", "e7e5", "g2g4");
        await factory.AppendAsync(owner, game.Id, 1, opening, afterOpening);

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
        var saved = await factory.LoadGameAsync(owner, game.Id);

        transactions.BothBegan.Should().BeTrue("the two appends must have raced");
        var values = outcomes.Select(outcome => outcome.Value).ToList();
        values.OfType<HalfMoveConflict>().Should().ContainSingle();
        var appended = values.OfType<MovesAppended>().Should().ContainSingle().Subject;

        var mateWon = values[0] is MovesAppended;
        var (winningMoves, winningStatus) = mateWon ? (mate, afterMate) : (quiet, afterQuiet);
        saved!.Moves.Should().Equal([.. opening, .. winningMoves]);
        saved.Rebuild().Status.Should().Be(winningStatus);
        saved.Status.Should().Be(mateWon ? GameProgress.Finished : GameProgress.Ongoing);
        saved.Result.Should().Be(mateWon ? GameResult.BlackWins : null);
        saved.EndReason.Should().Be(mateWon ? GameEndReason.Checkmate : null);
        saved.UpdatedAt.Should().Be(appended.UpdatedAt);
    }

    private AppDbContext NewContext(TransactionsHeldUntilBothBegin transactions) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(transactions)
            .Options);

    // Holds the first transaction to begin until the second has begun too.
    private sealed class TransactionsHeldUntilBothBegin : DbTransactionInterceptor
    {
        private readonly TwoArrivals _arrivals = new();

        public bool BothBegan => _arrivals.BothArrived;

        public override async ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            await _arrivals.ArriveAsync(cancellationToken);
            return result;
        }
    }
}
