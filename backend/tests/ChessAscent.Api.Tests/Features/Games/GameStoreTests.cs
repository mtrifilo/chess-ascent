using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ChessAscent.Api.Tests.Features.Games;

// Each call to the store gets a scope of its own, as a request would, so a
// load reads what the database holds rather than anything a context kept.
public sealed class GameStoreTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData(800)]
    public async Task CreateSavesAnOngoingGameWithNoMovesThatLoadsBackAsCreated(int? strength)
    {
        var owner = await factory.CreateOwnerAsync();
        var opponent = strength is { } rating ? Opponent.Computer(rating) : Opponent.None;

        var created = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.Black, opponent, GameData.StartingFen, Cancellation));
        var loaded = await factory.WithStoreAsync(store => store.LoadAsync(owner, created.Id, Cancellation));

        created.PlayerSide.Should().Be(Side.Black);
        created.Opponent.Should().Be(opponent);
        created.StartingFen.Should().Be(GameData.StartingFen);
        created.Status.Should().Be(GameProgress.Ongoing);
        created.Result.Should().BeNull();
        created.EndReason.Should().BeNull();
        created.UpdatedAt.Should().Be(created.CreatedAt);
        created.Moves.Should().BeEmpty();
        loaded.Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task LoadFindsNoGameThatBelongsToAnotherPlayerJustAsForAGameThatDoesNotExist()
    {
        var owner = await factory.CreateOwnerAsync();
        var otherPlayer = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));

        var asOtherPlayer = await factory.WithStoreAsync(store => store.LoadAsync(otherPlayer, game.Id, Cancellation));
        var unknown = await factory.WithStoreAsync(store => store.LoadAsync(owner, Guid.CreateVersion7(), Cancellation));

        asOtherPlayer.Should().BeNull();
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task AppendSavesOneMoveAndThenTwoAndTheGameRebuildsFromThem()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.Computer(1000), GameData.StartingFen, Cancellation));
        var board = GameData.Rules.NewGame();

        var (playerMove, afterPlayer) = board.Play("e2e4");
        var first = await factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, game.Id, 1, playerMove, afterPlayer, Cancellation));
        var (playerAndReply, afterReply) = board.Play("e7e5", "g1f3");
        var second = await factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, game.Id, 2, playerAndReply, afterReply, Cancellation));
        var loaded = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        first.Value.Should().BeOfType<MovesAppended>();
        var appended = second.Value.Should().BeOfType<MovesAppended>().Subject;
        loaded!.Moves.Should().Equal(new PlayedMove("e2e4", "e4"), new PlayedMove("e7e5", "e5"), new PlayedMove("g1f3", "Nf3"));
        loaded.Status.Should().Be(GameProgress.Ongoing);
        loaded.Result.Should().BeNull();
        loaded.EndReason.Should().BeNull();
        loaded.UpdatedAt.Should().Be(appended.UpdatedAt).And.BeAfter(game.CreatedAt);
        loaded.CreatedAt.Should().Be(game.CreatedAt);
        loaded.Rebuild().Fen.Should().Be(board.Fen);
    }

    [Fact]
    public async Task AppendRecordsHowTheGameEnded()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));
        var (foolsMate, afterMate) = GameData.Rules.NewGame().Play("f2f3", "e7e5", "g2g4", "d8h4");

        await factory.WithStoreAsync(store => store.AppendMovesAsync(owner, game.Id, 1, foolsMate, afterMate, Cancellation));
        var loaded = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        loaded!.Status.Should().Be(GameProgress.Finished);
        loaded.Result.Should().Be(GameResult.BlackWins);
        loaded.EndReason.Should().Be(GameEndReason.Checkmate);
        loaded.Moves.Select(move => move.San).Should().Equal("f3", "e5", "g4", "Qh4#");
        loaded.Rebuild().Status.Should().Be(afterMate);
    }

    // Every status the chess interface reports is stored as the matching
    // result and reason. The store does not check the moves against the
    // status, so one move serves for all of them.
    [Theory]
    [MemberData(nameof(BoardStatuses))]
    public async Task AppendStoresEachStatusTheChessInterfaceReports(
        string status, GameProgress progress, GameResult? result, GameEndReason? endReason)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));

        await factory.WithStoreAsync(store => store.AppendMovesAsync(
            owner, game.Id, 1, [new PlayedMove("e2e4", "e4")], StatusNamed(status), Cancellation));
        var loaded = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        loaded!.Status.Should().Be(progress);
        loaded.Result.Should().Be(result);
        loaded.EndReason.Should().Be(endReason);
    }

    public static TheoryData<string, GameProgress, GameResult?, GameEndReason?> BoardStatuses => new()
    {
        { "ongoing", GameProgress.Ongoing, null, null },
        { "white mates", GameProgress.Finished, GameResult.WhiteWins, GameEndReason.Checkmate },
        { "black mates", GameProgress.Finished, GameResult.BlackWins, GameEndReason.Checkmate },
        { "stalemate", GameProgress.Finished, GameResult.Draw, GameEndReason.Stalemate },
        { "threefold repetition", GameProgress.Finished, GameResult.Draw, GameEndReason.ThreefoldRepetition },
        { "fifty-move rule", GameProgress.Finished, GameResult.Draw, GameEndReason.FiftyMoveRule },
        { "dead position", GameProgress.Finished, GameResult.Draw, GameEndReason.DeadPosition },
    };

    [Fact]
    public async Task AppendAtAHalfMoveNumberAlreadySavedIsAConflictAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.Computer(600), GameData.StartingFen, Cancellation));
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        await factory.WithStoreAsync(store => store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation));
        var before = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        // A second request that loaded the game before the first move was
        // saved: its two moves start at the same half-move, and the status it
        // would write differs, so a stray write of it would show.
        var (stale, _) = GameData.Rules.NewGame().Play("f2f3", "e7e5");
        var outcome = await factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, game.Id, 1, stale, new Checkmate(Side.White), Cancellation));
        var after = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        outcome.Value.Should().Be(new HalfMoveConflict(1));
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task AppendToAnotherPlayersGameIsNotFoundJustAsForAGameThatDoesNotExistAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var otherPlayer = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");

        var asOtherPlayer = await factory.WithStoreAsync(store =>
            store.AppendMovesAsync(otherPlayer, game.Id, 1, e4, afterE4, Cancellation));
        var unknown = await factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, Guid.CreateVersion7(), 1, e4, afterE4, Cancellation));
        var loaded = await factory.WithStoreAsync(store => store.LoadAsync(owner, game.Id, Cancellation));

        asOtherPlayer.Value.Should().Be(new GameNotFound());
        unknown.Value.Should().Be(new GameNotFound());
        loaded.Should().BeEquivalentTo(game);
    }

    [Fact]
    public async Task AppendRefusesAnEmptyMoveListAndAHalfMoveNumberBelowOne()
    {
        var owner = await factory.CreateOwnerAsync();
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");

        var noMoves = () => factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, Guid.CreateVersion7(), 1, [], afterE4, Cancellation));
        var halfMoveZero = () => factory.WithStoreAsync(store =>
            store.AppendMovesAsync(owner, Guid.CreateVersion7(), 0, e4, afterE4, Cancellation));

        await noMoves.Should().ThrowAsync<ArgumentException>().WithParameterName("moves");
        await halfMoveZero.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("firstHalfMoveNumber");
    }

    // The context is the request's, and the caller may go on using it, so
    // the store leaves nothing in its change tracker: not what a load read,
    // and not what a create or an append wrote.
    [Fact]
    public async Task TheStoreLeavesNothingTrackedAfterLoadingOrSaving()
    {
        var owner = await factory.CreateOwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<GameStore>();
        var tracker = scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker;
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");

        var game = await store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation);
        var trackedAfterCreate = tracker.Entries().Count();
        await store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation);
        var trackedAfterAppend = tracker.Entries().Count();
        var conflict = await store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation);
        var trackedAfterConflict = tracker.Entries().Count();
        var loaded = await store.LoadAsync(owner, game.Id, Cancellation);
        var trackedAfterLoad = tracker.Entries().Count();

        conflict.Value.Should().BeOfType<HalfMoveConflict>();
        loaded!.Moves.Should().ContainSingle("the load must have read a move for its tracking to be tested");
        trackedAfterCreate.Should().Be(0);
        trackedAfterAppend.Should().Be(0);
        trackedAfterConflict.Should().Be(0);
        trackedAfterLoad.Should().Be(0);
    }

    // The enums are stored by name, so the table reads well in psql and
    // reordering an enum's members cannot change what a row means.
    [Fact]
    public async Task TheGamesTableStoresEnumerationsByName()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.Computer(1400), GameData.StartingFen, Cancellation));
        var (foolsMate, afterMate) = GameData.Rules.NewGame().Play("f2f3", "e7e5", "g2g4", "d8h4");
        await factory.WithStoreAsync(store => store.AppendMovesAsync(owner, game.Id, 1, foolsMate, afterMate, Cancellation));

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            """
            SELECT "PlayerSide", "OpponentKind", "OpponentStrength", "Status", "Result", "EndReason"
            FROM "Games" WHERE "Id" = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", game.Id);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue();

        reader.GetString(0).Should().Be("White");
        reader.GetString(1).Should().Be("Computer");
        reader.GetInt32(2).Should().Be(1400);
        reader.GetString(3).Should().Be("Finished");
        reader.GetString(4).Should().Be("BlackWins");
        reader.GetString(5).Should().Be("Checkmate");
    }

    [Fact]
    public async Task DeletingAnAccountDeletesItsGamesAndTheirMoves()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.WithStoreAsync(store =>
            store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation));
        var (moves, after) = GameData.Rules.NewGame().Play("e2e4", "e7e5");
        await factory.WithStoreAsync(store => store.AppendMovesAsync(owner, game.Id, 1, moves, after, Cancellation));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(owner);
            (await users.DeleteAsync(user!)).Succeeded.Should().BeTrue();
        }

        (await CountRowsAsync(game.Id)).Should().Be((0, 0));
    }

    private async Task<(long Games, long Moves)> CountRowsAsync(Guid gameId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM "Games" WHERE "Id" = @id),
                   (SELECT count(*) FROM "Moves" WHERE "GameId" = @id)
            """,
            connection);
        command.Parameters.AddWithValue("id", gameId);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        await reader.ReadAsync(Cancellation);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static GameStatus StatusNamed(string name) => name switch
    {
        "ongoing" => new Ongoing(),
        "white mates" => new Checkmate(Side.White),
        "black mates" => new Checkmate(Side.Black),
        "stalemate" => new Stalemate(),
        "threefold repetition" => new Draw(DrawReason.ThreefoldRepetition),
        "fifty-move rule" => new Draw(DrawReason.FiftyMoveRule),
        "dead position" => new Draw(DrawReason.DeadPosition),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such status in this test."),
    };
}
