using System.Net;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Hints;
using ChessAscent.Api.Security;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using static ChessAscent.Api.Tests.Features.Games.GameRequests;

namespace ChessAscent.Api.Tests.Features.Games;

// Hints in games against the computer, through the API, with a
// FakeHintProvider in place of the hint engine and a FakeOpponent in place of
// the computer, so each test says exactly what both answer.
public sealed class HintTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHintOnThePlayersTurnGivesTheTwoSquaresOfTheBestMoveWithoutPlayingIt()
    {
        var hints = FakeHintProvider.Hinting("g1f3");
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");

        using var response = await client.PostHintAsync(game.Id, 1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancellation)).Should().Be("""{"from":"g1","to":"f3"}""");
        hints.Calls.Should().ContainSingle().Which.Should().BeEmpty();
        var read = await client.ReadGameAsync(game.Id);
        read.Should().BeEquivalentTo(game with { HintCount = 1 }, InStrictOrder);
    }

    // The computer's first move comes with the new game, so the hint is for
    // Black's reply to it, and the provider is given that move.
    [Fact]
    public async Task AsBlackTheHintIsForTheReplyToTheComputersFirstMove()
    {
        var hints = FakeHintProvider.Hinting("e7e5");
        using var api = factory.WithFakes(FakeOpponent.Playing("e2e4"), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("black");

        var hint = await client.HintAsync(game);

        hint.Should().Be(new HintResponse("e7", "e5"));
        hints.Calls.Should().ContainSingle().Which.Should().Equal("e2e4");
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(1);
    }

    // A reload between the two steps of the reveal, a double click and a
    // retry each ask again in the same position. The engine is asked each
    // time, and the position is counted once.
    [Fact]
    public async Task TheCountIsOneAfterAHintStillOneAfterAskingAgainInTheSamePositionAndTwoAfterAHintInALaterOne()
    {
        var hints = FakeHintProvider.Hinting("e2e4");
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5"), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");

        var first = await client.HintAsync(game);
        var afterFirst = await client.ReadGameAsync(game.Id);
        var again = await client.HintAsync(game);
        var afterAgain = await client.ReadGameAsync(game.Id);
        var moved = await client.PlayAsync(game.Id, "e2e4");
        hints.Answer = (_, _) => Task.FromResult<HintReply>(new HintMove("g1f3"));
        var later = await client.HintAsync(moved);
        var afterLater = await client.ReadGameAsync(game.Id);

        game.HintCount.Should().Be(0);
        first.Should().Be(new HintResponse("e2", "e4"));
        afterFirst.HintCount.Should().Be(1);
        again.Should().Be(first);
        afterAgain.HintCount.Should().Be(1);
        moved.HintCount.Should().Be(1, "the move's answer carries the count");
        later.Should().Be(new HintResponse("g1", "f3"));
        afterLater.HintCount.Should().Be(2);
        hints.Calls.Select(moves => string.Join(' ', moves)).Should().Equal("", "", "e2e4 e7e5");
    }

    [Fact]
    public async Task AHintChangesNeitherTheGamesMovesNorItsStatusNorItsUpdateTime()
    {
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5"), FakeHintProvider.Hinting("g1f3"));
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        var moved = await client.PlayAsync(game.Id, "e2e4");
        var before = await postgres.SavedRowsApartFromHintsAsync(game.Id);

        await client.HintAsync(moved);

        before.Should().NotBeNull();
        (await postgres.SavedRowsApartFromHintsAsync(game.Id)).Should().Be(before);
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(moved with { HintCount = 1 }, InStrictOrder);
    }

    // The computer has Black and mates with Qh4 on the fourth half-move.
    [Fact]
    public async Task AHintInAFinishedGameIsRefusedWith409AndTheEngineIsNotAsked()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5", "d8h4"), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        var mated = await client.PlayAsync(game.Id, "f2f3", "g2g4");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostHintAsync(game.Id, mated.NextHalfMoveNumber());

        mated.Status.Should().Be(GameProgress.Finished);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.HintGameFinishedDetail);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        hints.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AHintInAGameWithNoOpponentIsRefusedWith422AndTheEngineIsNotAsked()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateGameAsync("white");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostHintAsync(game.Id, 1);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.HintWithoutComputerDetail);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        hints.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherPlayersGameIsNotFoundAndTheEngineIsNotAsked()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var owner = await api.SignedInClientAsync();
        var game = await owner.CreateComputerGameAsync("white");
        using var otherPlayer = await api.SignedInClientAsync();

        using var response = await otherPlayer.PostHintAsync(game.Id, 1);
        using var noSuchGame = await owner.PostHintAsync(Guid.NewGuid(), 1);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameNotFoundDetail);
        (await response.ComparableBodyAsync()).Should().Be(await noSuchGame.ComparableBodyAsync());
        (await owner.ReadGameAsync(game.Id)).HintCount.Should().Be(0);
        hints.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ASignedOutHintRequestIs401AndChangesNothing()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var owner = await api.SignedInClientAsync();
        var game = await owner.CreateComputerGameAsync("white");
        using var signedOut = api.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await signedOut.PostHintAsync(game.Id, 1);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        await response.ReadProblemAsync();
        (await owner.ReadGameAsync(game.Id)).HintCount.Should().Be(0);
        hints.Calls.Should().BeEmpty();
    }

    // The refusal of cross-site requests applies to every POST, so a page on
    // another site cannot spend a hint in the player's game with their cookie.
    [Fact]
    public async Task AHintRequestStartedByAnotherSiteIsRefusedAndChangesNothing()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");

        using var forged = new HttpRequestMessage(HttpMethod.Post, $"/games/{game.Id}/hints")
        {
            Content = JsonContent.Create(new { halfMoveNumber = 1 }),
        };
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        forged.Headers.TryAddWithoutValidation(HeaderNames.Origin, "https://attacker.example").Should().BeTrue();
        using var response = await client.SendAsync(forged, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Detail.Should().Be(CrossSiteRequests.RefusedDetail);
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(0);
        hints.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"halfMoveNumber":null}""")]
    public async Task AHintRequestWithoutAHalfMoveNumberIsRefusedWith400UnderThatField(string body)
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");

        using var response = await client.PostAsync($"/games/{game.Id}/hints", JsonBody(body), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync<HttpValidationProblemDetails>()).Errors.Keys.Should().Equal("HalfMoveNumber");
        hints.Calls.Should().BeEmpty();
    }

    // After e4 and the computer's e5 the next half-move is 3: 1 and 2 have
    // been played, 4 is ahead, and 0 is never a half-move.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task AHintForAnotherHalfMoveThanTheNextIsRefusedWith409BeforeTheEngineIsAsked(int halfMoveNumber)
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5"), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        await client.PlayAsync(game.Id, "e2e4");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostHintAsync(game.Id, halfMoveNumber);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.HintConflictDetail);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        hints.Calls.Should().BeEmpty();
    }

    // Saved through the store, as no request can save it: the player's move
    // without the computer's reply, which leaves the computer to move.
    [Fact]
    public async Task AHintInAGameSavedWithTheComputerToMoveAnswers500AndTheEngineIsNotAsked()
    {
        var hints = new FakeHintProvider();
        var logs = new CapturingLoggerProvider();
        using var api = factory.WithFakes(new FakeOpponent(), hints, logs);
        var (signedIn, ownerId) = await api.SignedInPlayerAsync();
        using var client = signedIn;
        var game = await factory.CreateGameAsync(ownerId, Opponent.Computer(800), Side.White);
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        await factory.AppendAsync(ownerId, game.Id, 1, e4, afterE4);
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostHintAsync(game.Id, 2);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Detail.Should().BeNull();
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        hints.Calls.Should().BeEmpty();
        logs.Messages.Where(message => message.StartsWith("Computer game", StringComparison.Ordinal))
            .Should().Equal($"Computer game {game.Id} cannot be played: it is saved with the computer to move.");
    }

    [Fact]
    public async Task WhenTheEngineGivesNoHintNothingIsRecordedAndTheSameRequestThenSucceeds()
    {
        var failures = 1;
        var hints = new FakeHintProvider
        {
            Answer = (_, _) => Task.FromResult<HintReply>(Interlocked.Decrement(ref failures) >= 0
                ? new HintFailed("secret-reason-for-the-log")
                : new HintMove("e2e4")),
        };
        using var api = factory.WithFakes(new FakeOpponent(), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var failed = await client.PostHintAsync(game.Id, 1);
        var after = await postgres.SavedRowsAsync(game.Id);
        using var retried = await client.PostHintAsync(game.Id, 1);

        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await failed.Content.ReadAsStringAsync(Cancellation)).Should().NotContain("secret");
        (await failed.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.HintUnavailableDetail);
        before.Should().NotBeNull();
        after.Should().Be(before);
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retried.ReadHintAsync()).Should().Be(new HintResponse("e2", "e4"));
        (await client.ReadGameAsync(game.Id)).HintCount.Should().Be(1);
    }

    // Answers that mean the server is at fault. The illegal move is one for
    // Black when White is to move, which the provider should never return,
    // since it checks its own moves.
    public static TheoryData<string, string> FaultyHints => new()
    {
        { "illegal move", "the hint engine chose a move that is not legal in the position" },
        { "invalid history", "the hint engine refused half-move 1 of the game" },
        { "game over", "the hint engine found the game already over" },
    };

    [Theory]
    [MemberData(nameof(FaultyHints))]
    public async Task AHintThatMeansTheServerIsAtFaultAnswers500RecordsNothingAndIsLoggedWithTheGameId(string reply, string reason)
    {
        var hints = new FakeHintProvider { Answer = (_, _) => Task.FromResult(FaultyHint(reply)) };
        var logs = new CapturingLoggerProvider();
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5"), hints, logs);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        var moved = await client.PlayAsync(game.Id, "e2e4");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostHintAsync(game.Id, moved.NextHalfMoveNumber());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Detail.Should().BeNull();
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        logs.Messages.Where(message => message.StartsWith("No hint", StringComparison.Ordinal))
            .Should().Equal($"No hint can be given in game {game.Id}: {reason}.");
    }

    // The player moves in another tab while the engine looks for the hint,
    // so the hint is for a position the game has left.
    [Fact]
    public async Task AHintFoundWhileAMoveIsSavedIsRefusedWith409AndNotCounted()
    {
        var hints = new FakeHintProvider();
        using var api = factory.WithFakes(FakeOpponent.Playing("e7e5"), hints);
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");
        hints.Answer = async (_, _) =>
        {
            await client.PlayAsync(game.Id, "d2d4");
            return new HintMove("e2e4");
        };

        using var response = await client.PostHintAsync(game.Id, 1);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.HintConflictDetail);
        var read = await client.ReadGameAsync(game.Id);
        read.Moves.Select(move => move.Uci).Should().Equal("d2d4", "e7e5");
        read.HintCount.Should().Be(0);
    }

    // The list reads the count from the game's own row, so each row shows
    // what the game's response shows.
    [Fact]
    public async Task EachRowOfTheListCarriesTheCountWhichAHintMoves()
    {
        using var api = factory.WithFakes(new FakeOpponent(), FakeHintProvider.Hinting("e2e4"));
        using var client = await api.SignedInClientAsync();
        var hinted = await client.CreateComputerGameAsync("white");
        var unhinted = await client.CreateComputerGameAsync("white");

        var before = await client.ListGamesAsync("ongoing");
        await client.HintAsync(hinted);
        var after = await client.ListGamesAsync("ongoing");

        before.Games.Select(row => (row.Id, row.HintCount)).Should().BeEquivalentTo(new[] { (hinted.Id, 0), (unhinted.Id, 0) });
        after.Games.Select(row => (row.Id, row.HintCount)).Should().BeEquivalentTo(new[] { (hinted.Id, 1), (unhinted.Id, 0) });
    }

    private static HintReply FaultyHint(string reply) => reply switch
    {
        "illegal move" => new HintMove("e7e5"),
        "invalid history" => new InvalidMoveInHistory(0, new IllegalMove("e2e4")),
        "game over" => new GameAlreadyOver(new Checkmate(Side.White)),
        _ => throw new ArgumentOutOfRangeException(nameof(reply), reply, null),
    };
}
