using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ChessAscent.Api.Tests.Features.Games;

// GameStore.ResignAsync against the real database, alone and racing an
// append to the same game.
public sealed class ResignStoreTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Side.White, GameResult.BlackWins)]
    [InlineData(Side.Black, GameResult.WhiteWins)]
    public async Task ResignEndsTheGameAsALossForThePlayersSideAndKeepsItsMovesAndHintCount(Side playerSide, GameResult result)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner, playerSide: playerSide);
        var (moves, status) = GameData.Rules.NewGame().Play("e2e4", "e7e5");
        await factory.AppendAsync(owner, game.Id, 1, moves, status);
        (await factory.CountHintAsync(owner, game.Id, 3)).Value.Should().Be(new HintCounted());
        var before = await factory.LoadGameAsync(owner, game.Id);

        var outcome = await factory.WithStoreAsync(store => store.ResignAsync(owner, game.Id, Cancellation));
        var saved = await factory.LoadGameAsync(owner, game.Id);

        var resigned = outcome.Value.Should().BeOfType<GameResigned>().Subject;
        saved!.Status.Should().Be(GameProgress.Finished);
        saved.Result.Should().Be(result);
        saved.EndReason.Should().Be(GameEndReason.Resignation);
        saved.Moves.Should().Equal(before!.Moves);
        saved.HintCount.Should().Be(1);
        saved.UpdatedAt.Should().Be(resigned.UpdatedAt).And.BeOnOrAfter(before.UpdatedAt);
        saved.CreatedAt.Should().Be(before.CreatedAt);
    }

    [Fact]
    public async Task ResignInAFinishedGameIsRefusedAndTheResultStands()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var (moves, status) = GameData.Rules.NewGame().Play(GameRequests.FoolsMate);
        await factory.AppendAsync(owner, game.Id, 1, moves, status);
        var saved = await postgres.SavedRowsAsync(game.Id);

        var outcome = await factory.WithStoreAsync(store => store.ResignAsync(owner, game.Id, Cancellation));

        outcome.Value.Should().Be(new GameFinished());
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    [Fact]
    public async Task ResignInAnotherPlayersGameIsNotFoundJustAsForAGameThatDoesNotExistAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var otherPlayer = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var saved = await postgres.SavedRowsAsync(game.Id);

        var inOthers = await factory.WithStoreAsync(store => store.ResignAsync(otherPlayer, game.Id, Cancellation));
        var inUnknown = await factory.WithStoreAsync(store => store.ResignAsync(otherPlayer, Guid.CreateVersion7(), Cancellation));

        inOthers.Value.Should().Be(new GameNotFound());
        inUnknown.Value.Should().Be(new GameNotFound());
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    // The player has Black, so a resignation is a win for White, and a mate
    // by Black's Qh4 a win for Black: whichever finished the game shows.
    // The append is held just after its update has locked the game's row,
    // and let go only once PostgreSQL reports the resignation's update
    // waiting for that lock.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AResignationThatWaitsOnAnAppendEndsTheGameAfterItsMovesUnlessTheyEndedIt(bool appendMates)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner, playerSide: Side.Black);
        var board = GameData.Rules.NewGame();
        var (opening, afterOpening) = board.Play("f2f3", "e7e5", "g2g4");
        await factory.AppendAsync(owner, game.Id, 1, opening, afterOpening);
        var (appendMoves, appendStatus) = appendMates ? board.Play("d8h4") : board.Play("a7a6", "a2a3");

        var hold = HoldTheFirstCommand.GameUpdates();
        await using var appendContext = NewContext(hold);
        await using var resignContext = NewContext();

        var append = Task.Run(
            () => new GameStore(appendContext, TimeProvider.System)
                .AppendMovesAsync(owner, game.Id, 4, appendMoves, appendStatus, Cancellation),
            Cancellation);
        await hold.FirstRan.WaitAsync(Patience, Cancellation);
        var resign = Task.Run(
            () => new GameStore(resignContext, TimeProvider.System).ResignAsync(owner, game.Id, Cancellation),
            Cancellation);
        var resignWaitedOnTheLock = await AGameUpdateWaitsOnAsync(hold.HeldBackend);
        hold.Release();
        var appendOutcome = await append;
        var resignOutcome = await resign;
        var saved = await factory.LoadGameAsync(owner, game.Id);

        resignWaitedOnTheLock.Should().BeTrue("the resignation must have waited for the append's row lock");
        appendOutcome.Value.Should().BeOfType<MovesAppended>();
        saved!.Moves.Should().Equal([.. opening, .. appendMoves]);
        saved.Status.Should().Be(GameProgress.Finished);
        if (appendMates)
        {
            resignOutcome.Value.Should().Be(new GameFinished());
            saved.Result.Should().Be(GameResult.BlackWins);
            saved.EndReason.Should().Be(GameEndReason.Checkmate);
        }
        else
        {
            var resigned = resignOutcome.Value.Should().BeOfType<GameResigned>().Subject;
            saved.Result.Should().Be(GameResult.WhiteWins);
            saved.EndReason.Should().Be(GameEndReason.Resignation);
            saved.UpdatedAt.Should().Be(resigned.UpdatedAt);
        }
    }

    // The resignation is held just after its update has locked the game's
    // row, so the append's update waits for that lock; once the resignation
    // commits, the append finds the game over.
    [Fact]
    public async Task AnAppendThatWaitsOnAResignationIsRefusedAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var board = GameData.Rules.NewGame();
        var (opening, afterOpening) = board.Play("e2e4");
        await factory.AppendAsync(owner, game.Id, 1, opening, afterOpening);
        var (appendMoves, appendStatus) = board.Play("e7e5");

        var hold = HoldTheFirstCommand.GameUpdates();
        await using var resignContext = NewContext(hold);
        await using var appendContext = NewContext();

        var resign = Task.Run(
            () => new GameStore(resignContext, TimeProvider.System).ResignAsync(owner, game.Id, Cancellation),
            Cancellation);
        await hold.FirstRan.WaitAsync(Patience, Cancellation);
        var append = Task.Run(
            () => new GameStore(appendContext, TimeProvider.System)
                .AppendMovesAsync(owner, game.Id, 2, appendMoves, appendStatus, Cancellation),
            Cancellation);
        var appendWaitedOnTheLock = await AGameUpdateWaitsOnAsync(hold.HeldBackend);
        hold.Release();
        var resignOutcome = await resign;
        var appendOutcome = await append;
        var saved = await factory.LoadGameAsync(owner, game.Id);

        appendWaitedOnTheLock.Should().BeTrue("the append must have waited for the resignation's row lock");
        var resigned = resignOutcome.Value.Should().BeOfType<GameResigned>().Subject;
        appendOutcome.Value.Should().Be(new GameFinished());
        saved!.Moves.Should().Equal(opening);
        saved.Status.Should().Be(GameProgress.Finished);
        saved.Result.Should().Be(GameResult.BlackWins);
        saved.EndReason.Should().Be(GameEndReason.Resignation);
        saved.UpdatedAt.Should().Be(resigned.UpdatedAt);
    }

    private AppDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(interceptors)
            .Options);

    // Asks PostgreSQL, from a connection of its own, whether an update of a
    // game is blocked by the backend that holds the first update's lock,
    // polling until one is or patience runs out.
    private async Task<bool> AGameUpdateWaitsOnAsync(int heldBackend)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE @held = ANY(pg_blocking_pids(pid)) AND query LIKE 'UPDATE "Games"%'
            """,
            connection);
        command.Parameters.AddWithValue("held", heldBackend);

        var giveUpAt = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < giveUpAt)
        {
            if (await command.ExecuteScalarAsync(Cancellation) is > 0L)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Cancellation);
        }

        return false;
    }
}
