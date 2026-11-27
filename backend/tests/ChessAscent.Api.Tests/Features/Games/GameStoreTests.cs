using ChessAscent.Api.Database;
using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ChessAscent.Api.Tests.Features.Games;

// Each call to the store gets a scope of its own, as a request would, so a
// load reads what the database holds rather than anything a context kept.
public sealed class GameStoreTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    // A move saved at half-move 3 of a game with one move, as something other
    // than the store might write it, leaving half-move 2 empty.
    private const string GapAtHalfMoveTwo =
        """INSERT INTO "Moves" ("GameId", "HalfMoveNumber", "Uci", "San") VALUES (@id, 3, 'g1f3', 'Nf3')""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData(800)]
    public async Task CreateSavesAnOngoingGameWithNoMovesThatLoadsBackAsCreated(int? strength)
    {
        var owner = await factory.CreateOwnerAsync();
        var opponent = strength is { } rating ? Opponent.Computer(rating) : Opponent.None;

        var created = await factory.CreateGameAsync(owner, opponent, Side.Black);
        var loaded = await factory.LoadGameAsync(owner, created.Id);

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
        var game = await factory.CreateGameAsync(owner);

        var asOtherPlayer = await factory.LoadGameAsync(otherPlayer, game.Id);
        var unknown = await factory.LoadGameAsync(owner, Guid.CreateVersion7());

        asOtherPlayer.Should().BeNull();
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task AppendSavesOneMoveAndThenTwoAndTheGameRebuildsFromThem()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner, Opponent.Computer(1000));
        var board = GameData.Rules.NewGame();

        var (playerMove, afterPlayer) = board.Play("e2e4");
        var first = await factory.AppendAsync(owner, game.Id, 1, playerMove, afterPlayer);
        var (playerAndReply, afterReply) = board.Play("e7e5", "g1f3");
        var second = await factory.AppendAsync(owner, game.Id, 2, playerAndReply, afterReply);
        var loaded = await factory.LoadGameAsync(owner, game.Id);

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
        var game = await factory.CreateGameAsync(owner);
        var (foolsMate, afterMate) = GameData.Rules.NewGame().Play("f2f3", "e7e5", "g2g4", "d8h4");

        await factory.AppendAsync(owner, game.Id, 1, foolsMate, afterMate);
        var loaded = await factory.LoadGameAsync(owner, game.Id);

        loaded!.Status.Should().Be(GameProgress.Finished);
        loaded.Result.Should().Be(GameResult.BlackWins);
        loaded.EndReason.Should().Be(GameEndReason.Checkmate);
        loaded.Moves.Select(move => move.San).Should().Equal("f3", "e5", "g4", "Qh4#");
        loaded.Rebuild().Status.Should().Be(afterMate);
    }

    // Every status the chess interface reports is stored as the matching
    // result and reason; draws are covered by the next test, one per reason.
    // The store does not check the moves against the status, so one move
    // serves for all of them.
    [Theory]
    [MemberData(nameof(BoardStatuses))]
    public async Task AppendStoresEachStatusTheChessInterfaceReports(
        string status, GameProgress progress, GameResult? result, GameEndReason? endReason)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);

        await factory.AppendAsync(owner, game.Id, 1, [new PlayedMove("e2e4", "e4")], StatusNamed(status));
        var loaded = await factory.LoadGameAsync(owner, game.Id);

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
    };

    // The compiler makes the mapping handle every DrawReason; this checks
    // that each is stored as the end reason of the same name, so a member
    // added later is covered without being listed here. A member meant to be
    // stored under another name fails here, and the rule in
    // GameStore.ToEndReason has to change with it.
    [Theory]
    [MemberData(nameof(DrawReasons))]
    public async Task AppendStoresEachDrawReasonAsTheEndReasonOfTheSameName(DrawReason reason)
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);

        await factory.AppendAsync(owner, game.Id, 1, [new PlayedMove("e2e4", "e4")], new Draw(reason));
        var loaded = await factory.LoadGameAsync(owner, game.Id);

        loaded!.Status.Should().Be(GameProgress.Finished);
        loaded.Result.Should().Be(GameResult.Draw);
        Enum.TryParse<GameEndReason>(reason.ToString(), out var sameName)
            .Should().BeTrue($"GameEndReason needs a member named {reason}");
        loaded.EndReason.Should().Be(sameName);
    }

    public static TheoryData<DrawReason> DrawReasons => new(Enum.GetValues<DrawReason>());

    [Fact]
    public async Task AppendAtAHalfMoveNumberAlreadySavedIsAConflictAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner, Opponent.Computer(600));
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        await factory.AppendAsync(owner, game.Id, 1, e4, afterE4);
        var before = await factory.LoadGameAsync(owner, game.Id);

        // A second request that loaded the game before the first move was
        // saved: its two moves start at the same half-move, and the status it
        // would write differs, so a stray write of it would show.
        var (stale, _) = GameData.Rules.NewGame().Play("f2f3", "e7e5");
        var outcome = await factory.AppendAsync(owner, game.Id, 1, stale, new Checkmate(Side.White));
        var after = await factory.LoadGameAsync(owner, game.Id);

        outcome.Value.Should().Be(new HalfMoveConflict());
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task AppendPastTheEndOfTheMovesIsAConflictAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var board = GameData.Rules.NewGame();
        var (e4, afterE4) = board.Play("e2e4");
        await factory.AppendAsync(owner, game.Id, 1, e4, afterE4);
        var before = await factory.LoadGameAsync(owner, game.Id);
        var (e5, afterE5) = board.Play("e7e5");

        var outcome = await factory.AppendAsync(owner, game.Id, before!.NextHalfMoveNumber + 1, e5, afterE5);
        var after = await factory.LoadGameAsync(owner, game.Id);

        outcome.Value.Should().Be(new HalfMoveConflict());
        after.Should().BeEquivalentTo(before);
    }

    // The count of saved moves is checked first; the key on (game, half-move
    // number) still refuses a duplicate when the numbers already have a gap
    // that something other than the store wrote.
    [Fact]
    public async Task AppendOntoAHalfMoveNumberTakenAfterAGapIsStillAConflict()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        await factory.AppendAsync(owner, game.Id, 1, e4, afterE4);
        await postgres.ExecuteAsync(GapAtHalfMoveTwo, game.Id);
        var before = await factory.LoadGameAsync(owner, game.Id);

        var outcome = await factory.AppendAsync(owner, game.Id, 3, [new PlayedMove("e7e5", "e5")], new Ongoing());
        var after = await factory.LoadGameAsync(owner, game.Id);

        before!.Moves.Should().HaveCount(2);
        outcome.Value.Should().Be(new HalfMoveConflict());
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task AppendToAFinishedGameIsRefusedAndTheResultStands()
    {
        var owner = await factory.CreateOwnerAsync();
        var otherPlayer = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var (foolsMate, afterMate) = GameData.Rules.NewGame().Play("f2f3", "e7e5", "g2g4", "d8h4");
        await factory.AppendAsync(owner, game.Id, 1, foolsMate, afterMate);
        var finished = await factory.LoadGameAsync(owner, game.Id);

        // As a stale request would send it: a move the board it had loaded
        // allowed, with that board's status.
        var outcome = await factory.AppendAsync(
            owner, game.Id, finished!.NextHalfMoveNumber, [new PlayedMove("a2a3", "a3")], new Ongoing());
        var asOtherPlayer = await factory.AppendAsync(
            otherPlayer, game.Id, finished.NextHalfMoveNumber, [new PlayedMove("a2a3", "a3")], new Ongoing());
        var after = await factory.LoadGameAsync(owner, game.Id);

        outcome.Value.Should().Be(new GameFinished());
        asOtherPlayer.Value.Should().Be(new GameNotFound(), "another player's finished game is not revealed either");
        after.Should().BeEquivalentTo(finished);
    }

    [Fact]
    public async Task AppendToAnotherPlayersGameIsNotFoundJustAsForAGameThatDoesNotExistAndSavesNothing()
    {
        var owner = await factory.CreateOwnerAsync();
        var otherPlayer = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");

        var asOtherPlayer = await factory.AppendAsync(otherPlayer, game.Id, 1, e4, afterE4);
        var unknown = await factory.AppendAsync(owner, Guid.CreateVersion7(), 1, e4, afterE4);
        var loaded = await factory.LoadGameAsync(owner, game.Id);

        asOtherPlayer.Value.Should().Be(new GameNotFound());
        unknown.Value.Should().Be(new GameNotFound());
        loaded.Should().BeEquivalentTo(game);
    }

    // All are refused before the database is reached, so no account or game
    // is needed.
    [Fact]
    public async Task AppendRefusesAnEmptyMoveListAnEmptyMoveAndAHalfMoveNumberBelowOne()
    {
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");

        var noMoves = () => factory.AppendAsync("unused", Guid.CreateVersion7(), 1, [], afterE4);
        var emptyMove = () => factory.AppendAsync("unused", Guid.CreateVersion7(), 1, [default], afterE4);
        var halfMoveZero = () => factory.AppendAsync("unused", Guid.CreateVersion7(), 0, e4, afterE4);

        await noMoves.Should().ThrowAsync<ArgumentException>().WithParameterName("moves");
        await emptyMove.Should().ThrowAsync<ArgumentException>().WithParameterName("moves");
        await halfMoveZero.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("firstHalfMoveNumber");
    }

    // SaveChanges saves everything the context tracks, so a save that began
    // with the caller's own changes pending would save them too, or fail on
    // them.
    [Fact]
    public async Task CreateAndAppendRefuseToStartWhileTheContextHoldsUnsavedChanges()
    {
        var owner = await factory.CreateOwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<GameStore>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation);
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        var account = await context.Users.SingleAsync(user => user.Id == owner, Cancellation);
        account.PhoneNumber = "unsaved";

        var create = () => store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation);
        var append = () => store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation);

        await create.Should().ThrowAsync<InvalidOperationException>();
        await append.Should().ThrowAsync<InvalidOperationException>();
        (await factory.LoadGameAsync(owner, game.Id)).Should().BeEquivalentTo(game);
    }

    // The context is the request's, and the caller may go on using it, so
    // the store leaves nothing in its change tracker on any path: a load, a
    // create that succeeds or fails, an append that succeeds, one refused by
    // the count of saved moves, and one refused by the key after its rows
    // were added. A failed row left tracked would be inserted again by the
    // caller's next SaveChanges.
    [Fact]
    public async Task TheStoreLeavesNothingTrackedAfterLoadingOrSaving()
    {
        var owner = await factory.CreateOwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<GameStore>();
        var tracker = scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker;
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        var tracked = new List<int>();

        var failedCreate = () => store.CreateAsync(
            "no-such-account", Side.White, Opponent.None, GameData.StartingFen, Cancellation);
        await failedCreate.Should().ThrowAsync<DbUpdateException>();
        tracked.Add(tracker.Entries().Count());
        var game = await store.CreateAsync(owner, Side.White, Opponent.None, GameData.StartingFen, Cancellation);
        tracked.Add(tracker.Entries().Count());
        await store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation);
        tracked.Add(tracker.Entries().Count());
        var countConflict = await store.AppendMovesAsync(owner, game.Id, 1, e4, afterE4, Cancellation);
        tracked.Add(tracker.Entries().Count());
        await postgres.ExecuteAsync(GapAtHalfMoveTwo, game.Id);
        var keyConflict = await store.AppendMovesAsync(
            owner, game.Id, 3, [new PlayedMove("e7e5", "e5")], new Ongoing(), Cancellation);
        tracked.Add(tracker.Entries().Count());
        var loaded = await store.LoadAsync(owner, game.Id, Cancellation);
        tracked.Add(tracker.Entries().Count());

        countConflict.Value.Should().BeOfType<HalfMoveConflict>();
        keyConflict.Value.Should().BeOfType<HalfMoveConflict>();
        loaded!.Moves.Should().HaveCount(2, "the load must have read moves for its tracking to be tested");
        tracked.Should().AllBeEquivalentTo(0);
    }

    // The enums are stored by name, so the table reads well in psql and
    // reordering an enum's members cannot change what a row means.
    [Fact]
    public async Task TheGamesTableStoresEnumerationsByName()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner, Opponent.Computer(1400));
        var (foolsMate, afterMate) = GameData.Rules.NewGame().Play("f2f3", "e7e5", "g2g4", "d8h4");
        await factory.AppendAsync(owner, game.Id, 1, foolsMate, afterMate);

        var row = await ReadRowAsync(
            """
            SELECT "PlayerSide", "OpponentKind", "OpponentStrength", "Status", "Result", "EndReason"
            FROM "Games" WHERE "Id" = @id
            """,
            game.Id);

        row.Should().Equal("White", "Computer", 1400, "Finished", "BlackWins", "Checkmate");
    }

    // The stored text is each member's name, so rows already hold these
    // names. Adding a member or reordering them is safe; renaming or removing
    // one of these leaves rows unreadable until a migration rewrites them.
    [Fact]
    public void TheStoredEnumerationNamesAreTheOnesRowsHold()
    {
        Enum.GetNames<Side>().Should().Contain(["White", "Black"]);
        Enum.GetNames<OpponentKind>().Should().Contain(["None", "Computer"]);
        Enum.GetNames<GameProgress>().Should().Contain(["Ongoing", "Finished"]);
        Enum.GetNames<GameResult>().Should().Contain(["WhiteWins", "BlackWins", "Draw"]);
        Enum.GetNames<GameEndReason>().Should().Contain(
            ["Checkmate", "Stalemate", "ThreefoldRepetition", "FiftyMoveRule", "DeadPosition", "Resignation"]);
    }

    [Fact]
    public async Task DeletingAnAccountDeletesItsGamesAndTheirMoves()
    {
        var owner = await factory.CreateOwnerAsync();
        var game = await factory.CreateGameAsync(owner);
        var (moves, after) = GameData.Rules.NewGame().Play("e2e4", "e7e5");
        await factory.AppendAsync(owner, game.Id, 1, moves, after);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(owner);
            (await users.DeleteAsync(user!)).Succeeded.Should().BeTrue();
        }

        var counts = await ReadRowAsync(
            """
            SELECT (SELECT count(*) FROM "Games" WHERE "Id" = @id),
                   (SELECT count(*) FROM "Moves" WHERE "GameId" = @id)
            """,
            game.Id);

        counts.Should().Equal(0L, 0L);
    }

    // Reads the database directly, bypassing EF Core, to see what is stored.
    private async Task<object[]> ReadRowAsync(string sql, Guid gameId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", gameId);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue();
        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        return values;
    }

    private static GameStatus StatusNamed(string name) => name switch
    {
        "ongoing" => new Ongoing(),
        "white mates" => new Checkmate(Side.White),
        "black mates" => new Checkmate(Side.Black),
        "stalemate" => new Stalemate(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such status in this test."),
    };
}
