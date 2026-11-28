using System.Net;
using System.Text.Json.Nodes;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Features.Accounts;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using static ChessAscent.Api.Tests.Features.Games.GameRequests;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// Games against the computer, through the API, with a FakeOpponent in place
// of the real one, so each test says exactly what the computer answers.
public sealed class ComputerGameTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Scholar's mate: the player has White and mates with Qxf7 on the
    // seventh half-move, after the computer's e5, Nc6 and Nf6.
    [Fact]
    public async Task AsWhiteTheComputerRepliesToEachMoveUntilThePlayerMates()
    {
        var opponent = FakeOpponent.Playing("e7e5", "b8c6", "g8f6");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");

        var afterFirst = await client.PlayAsync(game.Id, "e2e4");
        var afterSecond = await client.PlayAsync(game.Id, "f1c4");
        var afterThird = await client.PlayAsync(game.Id, "d1h5");
        var mated = await client.PlayAsync(game.Id, "h5f7");

        game.Moves.Should().BeEmpty();
        game.Opponent.Should().Be(new OpponentResponse(OpponentKind.Computer, 800));
        afterFirst.Moves.Should().Equal(new MoveResponse(1, "e2e4", "e4"), new MoveResponse(2, "e7e5", "e5"));
        afterFirst.SideToMove.Should().Be(Side.White);
        afterFirst.Status.Should().Be(GameProgress.Ongoing);
        afterFirst.LegalMoves.Should().ContainKey("f1");
        afterSecond.Moves.Select(move => move.San).Should().Equal("e4", "e5", "Bc4", "Nc6");
        afterThird.Moves.Select(move => move.San).Should().Equal("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6");
        mated.Moves.Select(move => move.San).Should().Equal("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#");
        mated.Status.Should().Be(GameProgress.Finished);
        mated.Result.Should().Be(GameResult.WhiteWins);
        mated.EndReason.Should().Be(GameEndReason.Checkmate);
        mated.LegalMoves.Should().BeEmpty();
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(mated, InStrictOrder);

        // Each time with the whole game so far, and never after the mate.
        opponent.Calls.Select(call => call.ToString()).Should().Equal(
            "[e2e4] at 800",
            "[e2e4 e7e5 f1c4] at 800",
            "[e2e4 e7e5 f1c4 b8c6 d1h5] at 800");
    }

    // Fool's mate with the player as Black: the computer's f3 comes with the
    // new game, g4 with the player's e5, and the player mates with Qh4.
    [Fact]
    public async Task AsBlackTheComputerMovesFirstWhenTheGameIsCreatedAndRepliesUntilThePlayerMates()
    {
        var opponent = FakeOpponent.Playing("f2f3", "g2g4");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);

        using var response = await client.PostComputerGameAsync("black", 1200);
        var created = await response.ReadGameAsync();
        var readAfterCreation = await client.ReadGameAsync(created.Id);
        var afterFirst = await client.PlayAsync(created.Id, "e7e5");
        var mated = await client.PlayAsync(created.Id, "d8h4");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.PlayerSide.Should().Be(Side.Black);
        created.Opponent.Should().Be(new OpponentResponse(OpponentKind.Computer, 1200));
        created.Moves.Should().Equal(new MoveResponse(1, "f2f3", "f3"));
        created.SideToMove.Should().Be(Side.Black);
        created.Fen.Should().Be("rnbqkbnr/pppppppp/8/8/8/5P2/PPPPP1PP/RNBQKBNR b KQkq - 0 1");
        created.LegalMoves.Should().ContainKey("e7");
        readAfterCreation.Should().BeEquivalentTo(created, InStrictOrder);
        afterFirst.Moves.Select(move => move.Uci).Should().Equal("f2f3", "e7e5", "g2g4");
        mated.Moves.Should().HaveCount(4);
        mated.Status.Should().Be(GameProgress.Finished);
        mated.Result.Should().Be(GameResult.BlackWins);
        mated.EndReason.Should().Be(GameEndReason.Checkmate);
        opponent.Calls.Select(call => call.ToString()).Should().Equal("[] at 1200", "[f2f3 e7e5] at 1200");
    }

    // The computer has Black and mates with Qh4 on the fourth half-move.
    [Fact]
    public async Task AReplyThatEndsTheGameIsSavedWithTheResultAndNothingMoreCanBePlayed()
    {
        var opponent = FakeOpponent.Playing("e7e5", "d8h4");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");
        await client.PlayAsync(game.Id, "f2f3");

        var mated = await client.PlayAsync(game.Id, "g2g4");
        using var afterTheEnd = await client.PostMoveAsync(game.Id, "a2a3", 5);

        mated.Moves.Select(move => move.San).Should().Equal("f3", "e5", "g4", "Qh4#");
        mated.Status.Should().Be(GameProgress.Finished);
        mated.Result.Should().Be(GameResult.BlackWins);
        mated.EndReason.Should().Be(GameEndReason.Checkmate);
        mated.LegalMoves.Should().BeEmpty();
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(mated, InStrictOrder);
        afterTheEnd.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await afterTheEnd.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameFinishedDetail);
        opponent.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenTheComputerGivesNoReplyNothingIsSavedAndTheSameMoveThenSucceeds()
    {
        var opponent = FakeOpponent.FailingOnceThenPlaying("e7e5", reasonForTheLog: "secret-reason-for-the-log");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var failed = await client.PostMoveAsync(game.Id, "e2e4", 1);
        var after = await postgres.SavedRowsAsync(game.Id);
        using var retried = await client.PostMoveAsync(game.Id, "e2e4", 1);

        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await failed.Content.ReadAsStringAsync(Cancellation)).Should().NotContain("secret");
        (await failed.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.OpponentUnavailableDetail);
        before.Should().NotBeNull();
        after.Should().Be(before);
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retried.ReadGameAsync()).Moves.Select(move => move.Uci).Should().Equal("e2e4", "e7e5");
        opponent.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenTheComputerGivesNoFirstMoveAsWhiteNoGameIsCreatedAndTryingAgainCreatesOne()
    {
        var opponent = FakeOpponent.FailingOnceThenPlaying("e2e4", reasonForTheLog: "secret-reason-for-the-log");
        using var api = ApiWith(opponent);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var failed = await client.PostComputerGameAsync("black", 800);
        var gamesAfterFailure = await postgres.CountGamesAsync(ownerId);
        using var retried = await client.PostComputerGameAsync("black", 800);

        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await failed.Content.ReadAsStringAsync(Cancellation)).Should().NotContain("secret");
        (await failed.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.OpponentUnavailableDetail);
        failed.Headers.Location.Should().BeNull();
        gamesAfterFailure.Should().Be(0);
        retried.StatusCode.Should().Be(HttpStatusCode.Created);
        (await retried.ReadGameAsync()).Moves.Should().Equal(new MoveResponse(1, "e2e4", "e4"));
        (await postgres.CountGamesAsync(ownerId)).Should().Be(1);
    }

    // Answers that mean the server is at fault, each after the player's
    // e2e4. The illegal and malformed moves are ones the opponent should
    // never have returned, since it checks its own moves.
    public static TheoryData<string, string> FaultyReplies => new()
    {
        { "illegal move", "it chose a move that is not legal in the position" },
        { "malformed move", "it chose something that is not a UCI move" },
        { "strength not offered", "it does not offer the strength 800" },
        { "invalid history", "it refused half-move 1 of the game" },
        { "game over", "it found the game already over" },
    };

    [Theory]
    [MemberData(nameof(FaultyReplies))]
    public async Task AReplyThatMeansTheServerIsAtFaultAnswers500SavesNothingAndIsLoggedWithTheGameId(string reply, string reason)
    {
        var opponent = new FakeOpponent { Answer = (_, _) => Task.FromResult(FaultyReply(reply)) };
        var logs = new CapturingLoggerProvider();
        using var api = ApiWith(opponent, logs);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostMoveAsync(game.Id, "e2e4", 1);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Detail.Should().BeNull();
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        logs.Messages.Where(message => message.StartsWith("Computer game", StringComparison.Ordinal))
            .Should().Equal($"Computer game {game.Id} cannot be played: {reason}.");
    }

    [Fact]
    public async Task AnIllegalFirstMoveAnswers500CreatesNoGameAndIsLogged()
    {
        // A move for Black, when White moves first.
        var opponent = new FakeOpponent { Answer = (_, _) => Task.FromResult<OpponentReply>(new OpponentMove("e7e5")) };
        var logs = new CapturingLoggerProvider();
        using var api = ApiWith(opponent, logs);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var response = await client.PostComputerGameAsync("black", 800);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await postgres.CountGamesAsync(ownerId)).Should().Be(0);
        logs.Messages.Where(message => message.StartsWith("A computer game", StringComparison.Ordinal))
            .Should().Equal("A computer game cannot be started: it chose a move that is not legal in the position.");
    }

    // Saved through the store, as no request can save it: the player's move
    // without the computer's reply, which leaves the computer to move.
    [Fact]
    public async Task AGameSavedWithTheComputerToMoveAnswers500AndThePlayersMoveIsNotPlayedForTheComputer()
    {
        var opponent = new FakeOpponent();
        var logs = new CapturingLoggerProvider();
        using var api = ApiWith(opponent, logs);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;
        var game = await factory.CreateGameAsync(ownerId, Opponent.Computer(800), Side.White);
        var (e4, afterE4) = GameData.Rules.NewGame().Play("e2e4");
        await factory.AppendAsync(ownerId, game.Id, 1, e4, afterE4);
        var before = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostMoveAsync(game.Id, "e7e5", 2);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        opponent.Calls.Should().BeEmpty();
        logs.Messages.Where(message => message.StartsWith("Computer game", StringComparison.Ordinal))
            .Should().Equal($"Computer game {game.Id} cannot be played: it is saved with the computer to move.");
    }

    // The opponent plays only from the standard starting position.
    [Fact]
    public async Task AComputerGameSavedFromAnotherStartingPositionAnswers500()
    {
        var opponent = new FakeOpponent();
        var logs = new CapturingLoggerProvider();
        using var api = ApiWith(opponent, logs);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;
        var game = await factory.CreateGameAsync(
            ownerId, Opponent.Computer(800), Side.White, startingFen: "k7/8/8/8/8/8/8/KR6 w - - 0 1");

        using var response = await client.PostMoveAsync(game.Id, "b1b2", 1);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await client.ReadGameAsync(game.Id)).Moves.Should().BeEmpty();
        opponent.Calls.Should().BeEmpty();
        logs.Messages.Where(message => message.StartsWith("Computer game", StringComparison.Ordinal))
            .Should().Equal($"Computer game {game.Id} cannot be played: it does not start from the standard starting position.");
    }

    // Sent one after the other, as a double click sends them: the second
    // carries a half-move number the first has taken.
    [Fact]
    public async Task AMoveSentTwiceInARowIsPlayedAndRepliedToOnce()
    {
        var opponent = FakeOpponent.Playing("e7e5");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");

        using var first = await client.PostMoveAsync(game.Id, "e2e4", 1);
        using var second = await client.PostMoveAsync(game.Id, "e2e4", 1);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.MoveConflictDetail);
        (await client.ReadGameAsync(game.Id)).Moves.Select(move => move.Uci).Should().Equal("e2e4", "e7e5");
        opponent.Calls.Should().HaveCount(1);
    }

    // Sent at the same moment: each request loads the game before either
    // has saved, and the opponent answers neither until it has been asked by
    // both, so both reach the save with a reply. The first to take the
    // game's row lock saves; the second then finds its half-move taken.
    [Fact]
    public async Task AMoveSentTwiceAtOnceIsSavedOnceWithOneReply()
    {
        var bothAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = 0;
        var opponent = new FakeOpponent
        {
            Answer = async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref asked) == 2)
                {
                    bothAsked.TrySetResult();
                }

                await bothAsked.Task.WaitAsync(Patience, cancellationToken);
                return new OpponentMove("e7e5");
            },
        };
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");

        var first = client.PostMoveAsync(game.Id, "e2e4", 1);
        var second = client.PostMoveAsync(game.Id, "e2e4", 1);
        using var firstResponse = await first;
        using var secondResponse = await second;

        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        opponent.Calls.Should().HaveCount(2);
        (await client.ReadGameAsync(game.Id)).Moves.Should().Equal(
            new MoveResponse(1, "e2e4", "e4"), new MoveResponse(2, "e7e5", "e5"));
    }

    // 2 is the computer's half-move, 1 has been played, and 4 is ahead.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task AMoveForAnotherHalfMoveThanTheNextIsRefusedWith409AndTheComputerIsNotAsked(int halfMoveNumber)
    {
        var opponent = FakeOpponent.Playing("e7e5");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");
        var afterFirst = await client.PlayAsync(game.Id, "e2e4");

        using var response = await client.PostMoveAsync(game.Id, "d2d4", halfMoveNumber);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.MoveConflictDetail);
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(afterFirst, InStrictOrder);
        opponent.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task AnotherPlayersComputerGameIsNotFoundAndTheComputerIsNotAsked()
    {
        var opponent = new FakeOpponent();
        using var api = ApiWith(opponent);
        using var owner = await SignedInClientAsync(api);
        var game = await owner.CreateComputerGameAsync("white");
        using var otherPlayer = await SignedInClientAsync(api);

        using var read = await otherPlayer.GetGameAsync(game.Id);
        using var move = await otherPlayer.PostMoveAsync(game.Id, "e2e4", 1);

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        move.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await move.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameNotFoundDetail);
        (await owner.ReadGameAsync(game.Id)).Moves.Should().BeEmpty();
        opponent.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AGameWithNoOpponentNeverAsksTheComputer()
    {
        var opponent = new FakeOpponent();
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateGameAsync("black");

        var played = await client.PlayAsync(game.Id, "e2e4", "e7e5");

        played.Opponent.Should().Be(new OpponentResponse(OpponentKind.None, null));
        played.Moves.Select(move => move.Uci).Should().Equal("e2e4", "e7e5");
        opponent.Calls.Should().BeEmpty();
    }

    public static TheoryData<int> Offered => new(OpponentStrengths.Offered);

    [Theory]
    [MemberData(nameof(Offered))]
    public async Task EveryOfferedStrengthIsSavedReadBackAndPlayedAt(int strength)
    {
        var opponent = FakeOpponent.Playing("e7e5");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);

        var created = await client.CreateComputerGameAsync("white", strength);
        var read = await client.ReadGameAsync(created.Id);
        await client.PlayAsync(created.Id, "e2e4");

        created.Opponent.Should().Be(new OpponentResponse(OpponentKind.Computer, strength));
        read.Opponent.Should().Be(new OpponentResponse(OpponentKind.Computer, strength));
        opponent.Calls.Single().Strength.Should().Be(strength);
    }

    [Theory]
    [InlineData("""{"playerSide":"black","opponent":{"kind":"computer"}}""")]
    [InlineData("""{"playerSide":"black","opponent":{"kind":"computer","strength":null}}""")]
    public async Task AComputerGameWithNoStrengthIsPlayedAt800(string body)
    {
        var opponent = FakeOpponent.Playing("e2e4");
        using var api = ApiWith(opponent);
        using var client = await SignedInClientAsync(api);

        using var response = await client.PostAsync("/games", JsonBody(body), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.ReadGameAsync();
        created.Opponent.Should().Be(new OpponentResponse(OpponentKind.Computer, 800));
        (await client.ReadGameAsync(created.Id)).Opponent.Strength.Should().Be(800);
        opponent.Calls.Single().Strength.Should().Be(800);
    }

    // As Black, so that a strength let through would reach the opponent.
    [Theory]
    [InlineData(700)]
    [InlineData(0)]
    [InlineData(-800)]
    [InlineData(599)]
    [InlineData(1500)]
    public async Task AStrengthTheComputerDoesNotOfferIsRefusedUnderTheStrengthFieldBeforeTheComputerIsAsked(int strength)
    {
        var opponent = new FakeOpponent();
        using var api = ApiWith(opponent);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var response = await client.PostComputerGameAsync("black", strength);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal("Opponent.Strength");
        problem.Errors["Opponent.Strength"].Should().Equal(GameEndpoints.UnofferedStrengthMessage);
        opponent.Calls.Should().BeEmpty();
        (await postgres.CountGamesAsync(ownerId)).Should().Be(0);
    }

    [Fact]
    public async Task AStrengthForAnOpponentOfKindNoneIsRefusedUnderTheStrengthField()
    {
        using var api = ApiWith(new FakeOpponent());
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var response = await client.PostGameAsync(new { playerSide = "white", opponent = new { kind = "none", strength = 800 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal("Opponent.Strength");
        problem.Errors["Opponent.Strength"].Should().Equal(GameEndpoints.StrengthWithoutComputerMessage);
        (await postgres.CountGamesAsync(ownerId)).Should().Be(0);
    }

    // Numbers are read only as JSON numbers (see JsonServices).
    [Theory]
    [InlineData("""{"playerSide":"black","opponent":{"kind":"computer","strength":"800"}}""")]
    [InlineData("""{"playerSide":"black","opponent":{"kind":"computer","strength":800.5}}""")]
    [InlineData("""{"playerSide":"black","opponent":{"kind":"computer","strength":true}}""")]
    public async Task AStrengthThatIsNotAWholeJsonNumberIsRefused(string body)
    {
        var opponent = new FakeOpponent();
        using var api = ApiWith(opponent);
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var response = await client.PostAsync("/games", JsonBody(body), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await response.ReadProblemAsync();
        opponent.Calls.Should().BeEmpty();
        (await postgres.CountGamesAsync(ownerId)).Should().Be(0);
    }

    [Fact]
    public async Task TheComputerAndItsStrengthAreWrittenAsTheOpponent()
    {
        using var api = ApiWith(new FakeOpponent());
        using var client = await SignedInClientAsync(api);

        using var response = await client.PostComputerGameAsync("white", 1000);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancellation))!.AsObject();

        body["opponent"]!.ToJsonString().Should().Be("""{"kind":"computer","strength":1000}""");
    }

    // The browser goes away while the computer is thinking. The opponent
    // either stops when told, as the real one does, or answers anyway, as
    // one that finished just then would; either way nothing is saved.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMoveWhoseRequestIsCancelledWhileTheComputerThinksSavesNothing(bool opponentStops)
    {
        var thinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toldToStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opponent = new FakeOpponent
        {
            // Thinks until told to stop, or for Patience if never told, which
            // then answers with a move that would be saved and fail the test.
            Answer = async (_, cancellationToken) =>
            {
                thinking.TrySetResult();
                try
                {
                    await Task.Delay(Patience, cancellationToken);
                }
                catch (OperationCanceledException) when (!opponentStops)
                {
                }
                finally
                {
                    toldToStop.TrySetResult(cancellationToken.IsCancellationRequested);
                }

                return new OpponentMove("e7e5");
            },
        };
        var abandoned = new AbandonedRequests();
        using var api = ApiWith(opponent, configure: services => services.AddSingleton<IStartupFilter>(abandoned));
        using var client = await SignedInClientAsync(api);
        var game = await client.CreateComputerGameAsync("white");
        var before = await postgres.SavedRowsAsync(game.Id);

        using var browser = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var move = client.PostAsJsonAsync($"/games/{game.Id}/moves", new { uci = "e2e4", halfMoveNumber = 1 }, browser.Token);
        await thinking.Task.WaitAsync(Patience, Cancellation);
        await browser.CancelAsync();
        var sent = async () => await move;
        await sent.Should().ThrowAsync<OperationCanceledException>();
        await abandoned.Finished.WaitAsync(Patience, Cancellation);

        (await toldToStop.Task).Should().BeTrue("the request's cancellation reaches the opponent");
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(before);
        opponent.Calls.Should().HaveCount(1);
    }

    // The browser goes away while the computer thinks about its first move,
    // and the opponent stops when told or answers anyway, as in the move
    // test above.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatingAGameAsBlackWhoseRequestIsCancelledWhileTheComputerThinksCreatesNothing(bool opponentStops)
    {
        var thinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toldToStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opponent = new FakeOpponent
        {
            Answer = async (_, cancellationToken) =>
            {
                thinking.TrySetResult();
                try
                {
                    await Task.Delay(Patience, cancellationToken);
                }
                catch (OperationCanceledException) when (!opponentStops)
                {
                }
                finally
                {
                    toldToStop.TrySetResult(cancellationToken.IsCancellationRequested);
                }

                return new OpponentMove("e2e4");
            },
        };
        var abandoned = new AbandonedRequests();
        using var api = ApiWith(opponent, configure: services => services.AddSingleton<IStartupFilter>(abandoned));
        var (signedIn, ownerId) = await SignedInPlayerAsync(api);
        using var client = signedIn;

        using var browser = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var create = client.PostAsJsonAsync("/games", new { playerSide = "black", opponent = new { kind = "computer" } }, browser.Token);
        await thinking.Task.WaitAsync(Patience, Cancellation);
        await browser.CancelAsync();
        var sent = async () => await create;
        await sent.Should().ThrowAsync<OperationCanceledException>();
        await abandoned.Finished.WaitAsync(Patience, Cancellation);

        (await toldToStop.Task).Should().BeTrue("the request's cancellation reaches the opponent");
        (await postgres.CountGamesAsync(ownerId)).Should().Be(0);
    }

    // The default is written out in OpponentRequest's documentation and
    // applied without being checked against the strengths offered.
    [Fact]
    public void TheDefaultStrengthIsOneTheComputerOffers() =>
        OpponentStrengths.Offered.Should().Contain(OpponentRequest.DefaultStrength);

    private static OpponentReply FaultyReply(string reply) => reply switch
    {
        "illegal move" => new OpponentMove("e2e4"),
        "malformed move" => new OpponentMove("zz99"),
        "strength not offered" => new StrengthNotOffered(800),
        "invalid history" => new InvalidMoveInHistory(0, new IllegalMove("e2e4")),
        "game over" => new GameAlreadyOver(new Checkmate(Side.White)),
        _ => throw new ArgumentOutOfRangeException(nameof(reply), reply, null),
    };

    // LEARN: ConfigureTestServices runs after Program.cs has registered the
    // application's services, so what it registers replaces what the
    // application chose, for this test's host only. It is the counterpart of
    // @MockitoBean (formerly @MockBean), or of a @TestConfiguration bean
    // marked @Primary, with two differences. Nothing is a mock unless the
    // test makes it one: here it is a hand-written fake. And there is no
    // @Primary to pick between two registrations; resolving a service that
    // is registered twice gives the last registration, so RemoveAll first
    // makes the fake the only one, rather than relying on that order.
    // WithWebHostBuilder builds a separate host from the class's factory, so
    // tests that replace the opponent differently can run at the same time.
    private WebApplicationFactory<Program> ApiWith(
        FakeOpponent opponent, CapturingLoggerProvider? logs = null, Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(host =>
        {
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOpponent>();
                services.AddSingleton<IOpponent>(opponent);
                configure?.Invoke(services);
            });
            if (logs is not null)
            {
                host.ConfigureLogging(logging => logging
                    .AddProvider(logs)
                    .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace));
            }
        });

    private async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> api) =>
        (await SignedInPlayerAsync(api)).Client;

    // A browser signed in to a new account, and the account's id.
    private async Task<(HttpClient Client, string OwnerId)> SignedInPlayerAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        var email = AccountData.NewEmail();
        await client.RegisterNewAsync(email);
        return (client, await factory.AccountIdAsync(email));
    }


    // Counts requests that the server has finished with after their client
    // gave up on them, so a test can wait for the server to be done with one
    // the client no longer hears about. A startup filter's middleware runs
    // before the application's own.
    private sealed class AbandonedRequests : IStartupFilter
    {
        public SemaphoreSlim Finished { get; } = new(0);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextStep) =>
            {
                try
                {
                    await nextStep(context);
                }
                finally
                {
                    if (context.RequestAborted.IsCancellationRequested)
                    {
                        Finished.Release();
                    }
                }
            });
            next(app);
        };
    }
}
