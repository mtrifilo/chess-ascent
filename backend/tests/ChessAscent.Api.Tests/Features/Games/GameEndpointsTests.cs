using System.Net;
using System.Text.Json.Nodes;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Security;
using ChessAscent.Api.Tests.Features.Accounts;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// Each test's HttpClient keeps the cookies the API sets, as a browser would,
// so a client that registered is that player's browser. Every game a test
// reads belongs to an account the test registered itself.
public sealed class GameEndpointsTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("white", Side.White)]
    [InlineData("black", Side.Black)]
    public async Task CreateStartsAGameFromTheStartingPositionThatReadsBackTheSame(string playerSide, Side expected)
    {
        using var client = await SignedInClientAsync();

        using var response = await client.PostGameAsync(playerSide);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.ReadGameAsync();
        response.Headers.Location.Should().Be(new Uri($"/games/{created.Id}", UriKind.Relative));
        created.PlayerSide.Should().Be(expected);
        created.Opponent.Should().Be(new OpponentResponse(OpponentKind.None, null));
        created.Fen.Should().Be(GameData.StartingFen);
        created.SideToMove.Should().Be(Side.White);
        created.Status.Should().Be(GameProgress.Ongoing);
        created.Result.Should().BeNull();
        created.EndReason.Should().BeNull();
        created.Moves.Should().BeEmpty();
        created.LegalMoves.Keys.Should().Equal("a2", "b1", "b2", "c2", "d2", "e2", "f2", "g1", "g2", "h2");
        created.LegalMoves["e2"].Should().Equal("e3", "e4");
        created.LegalMoves["g1"].Should().Equal("f3", "h3");
        created.LegalMoves.Values.Sum(squares => squares.Count).Should().Be(20);
        created.Promotions.Should().BeEmpty();
        (await client.ReadGameAsync(created.Id)).Should().BeEquivalentTo(created, InStrictOrder);
    }

    // The wire format itself, which the front end's generated types describe:
    // camel-case names, enumerations by name, and absent values as null.
    [Fact]
    public async Task TheGameIsWrittenWithCamelCaseNamesAndEnumerationsByName()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync("black");

        using var response = await client.PostMoveAsync(game.Id, "e2e4");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancellation))!.AsObject();

        body.Select(property => property.Key).Should().Equal(
            "id", "playerSide", "opponent", "fen", "sideToMove", "status", "result", "endReason",
            "moves", "legalMoves", "promotions");
        body["id"]!.GetValue<string>().Should().Be(game.Id.ToString());
        body["playerSide"]!.GetValue<string>().Should().Be("black");
        body["opponent"]!.ToJsonString().Should().Be("""{"kind":"none","strength":null}""");
        body["sideToMove"]!.GetValue<string>().Should().Be("black");
        body["status"]!.GetValue<string>().Should().Be("ongoing");
        body["result"].Should().BeNull();
        body["endReason"].Should().BeNull();
        body["moves"]!.ToJsonString().Should().Be("""[{"halfMoveNumber":1,"uci":"e2e4","san":"e4"}]""");
        body["legalMoves"]!["g8"]!.ToJsonString().Should().Be("""["f6","h6"]""");
    }

    [Fact]
    public async Task FoolsMateEndsTheGameWithAWinForBlackByCheckmate()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        var mated = await client.PlayAsync(game.Id, GameRequests.FoolsMate);

        mated.Status.Should().Be(GameProgress.Finished);
        mated.Result.Should().Be(GameResult.BlackWins);
        mated.EndReason.Should().Be(GameEndReason.Checkmate);
        mated.SideToMove.Should().Be(Side.White);
        mated.Fen.Should().Be("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");
        mated.Moves.Should().Equal(
            new MoveResponse(1, "f2f3", "f3"),
            new MoveResponse(2, "e7e5", "e5"),
            new MoveResponse(3, "g2g4", "g4"),
            new MoveResponse(4, "d8h4", "Qh4#"));
        mated.LegalMoves.Should().BeEmpty();
        mated.Promotions.Should().BeEmpty();
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(mated, InStrictOrder);
    }

    [Fact]
    public async Task EachMoveAnswersWithTheGameAsItThenReads()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        var afterFirst = await client.PlayAsync(game.Id, "e2e4");
        var readAfterFirst = await client.ReadGameAsync(game.Id);
        var afterSecond = await client.PlayAsync(game.Id, "c7c5");
        var readAfterSecond = await client.ReadGameAsync(game.Id);

        afterFirst.Should().BeEquivalentTo(readAfterFirst, InStrictOrder);
        afterFirst.SideToMove.Should().Be(Side.Black);
        afterFirst.LegalMoves["b8"].Should().Equal("a6", "c6");
        afterSecond.Should().BeEquivalentTo(readAfterSecond, InStrictOrder);
        afterSecond.Moves.Select(move => move.San).Should().Equal("e4", "c5");
    }

    public static TheoryData<string, string, GameResult, GameEndReason> Draws => new()
    {
        // Rb2 makes the hundredth half-move without a capture or a pawn move.
        { "k7/8/8/8/8/8/8/KR6 w - - 99 60", "b1b2", GameResult.Draw, GameEndReason.FiftyMoveRule },
        // Kxb2 leaves the two kings alone.
        { "k7/8/8/8/8/8/1r6/K7 w - - 0 1", "a1b2", GameResult.Draw, GameEndReason.DeadPosition },
        // Qb6 leaves Black's king no move, and it is not in check.
        { "k7/8/8/1Q6/8/8/8/K7 w - - 0 1", "b5b6", GameResult.Draw, GameEndReason.Stalemate },
    };

    // The API starts every game from the standard position, so these
    // positions are created through the store, as the game's owner.
    [Theory]
    [MemberData(nameof(Draws))]
    public async Task ADrawEndsTheGameAndSaysWhichRuleEndedIt(
        string startingFen, string uci, GameResult result, GameEndReason endReason)
    {
        var email = AccountData.NewEmail();
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(email);
        var ownerId = await factory.AccountIdAsync(email);
        var game = await factory.CreateGameAsync(ownerId, startingFen: startingFen);

        var drawn = await client.PlayAsync(game.Id, uci);

        drawn.Status.Should().Be(GameProgress.Finished);
        drawn.Result.Should().Be(result);
        drawn.EndReason.Should().Be(endReason);
        drawn.LegalMoves.Should().BeEmpty();
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(drawn, InStrictOrder);
    }

    [Fact]
    public async Task ThreefoldRepetitionEndsTheGameAsADraw()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        // The knights go out and back twice, so the starting position occurs
        // for the third time after the eighth half-move.
        var drawn = await client.PlayAsync(
            game.Id, "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8");

        drawn.Status.Should().Be(GameProgress.Finished);
        drawn.Result.Should().Be(GameResult.Draw);
        drawn.EndReason.Should().Be(GameEndReason.ThreefoldRepetition);
        drawn.Moves.Should().HaveCount(8);
        drawn.LegalMoves.Should().BeEmpty();
    }

    // After a4 b5 axb5 a6 bxa6 Bb7 axb7 Nc6, White's pawn on b7 can take the
    // rook on a8 or advance to b8, and either way must say what it becomes.
    [Fact]
    public async Task APawnThatCanPromoteIsListedWithThePiecesItMayBecome()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        var beforePromotion = await client.PlayAsync(
            game.Id, "a2a4", "b7b5", "a4b5", "a7a6", "b5a6", "c8b7", "a6b7", "b8c6");
        using var withoutPiece = await client.PostMoveAsync(game.Id, "b7a8");
        var promoted = await client.PlayAsync(game.Id, "b7a8n");

        PromotionPiece[] everyPiece = [PromotionPiece.Queen, PromotionPiece.Rook, PromotionPiece.Bishop, PromotionPiece.Knight];
        beforePromotion.LegalMoves["b7"].Should().Equal("a8", "b8");
        beforePromotion.Promotions.Should().BeEquivalentTo(
            [new PromotionResponse("b7", "a8", everyPiece), new PromotionResponse("b7", "b8", everyPiece)],
            options => options.WithStrictOrdering());
        withoutPiece.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        promoted.Moves[^1].Should().Be(new MoveResponse(9, "b7a8n", "bxa8=N"));
        promoted.Promotions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("e2e5")]
    [InlineData("e7e5")]
    [InlineData("e1g1")]
    [InlineData("e2e4q")]
    public async Task AnIllegalMoveIsRefusedWith422AndLeavesTheGameUnchanged(string uci)
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var response = await client.PostMoveAsync(game.Id, uci);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.IllegalMoveDetail);
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(game, InStrictOrder);
    }

    // Too long, which validation refuses before the chess interface reads
    // it, or the right length but not a move in UCI form.
    [Theory]
    [InlineData("e2e4qq")]
    [InlineData("e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4e2e4")]
    [InlineData("E2E4")]
    [InlineData("e2e9")]
    [InlineData("e2e4x")]
    [InlineData("e2e2")]
    [InlineData("e2")]
    [InlineData(" e2e4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AMalformedMoveIsRefusedWith400UnderTheUciFieldAndLeavesTheGameUnchanged(string? uci)
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var response = await client.PostMoveAsync(game.Id, uci);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal(nameof(PlayMoveRequest.Uci));
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(game, InStrictOrder);
    }

    [Fact]
    public async Task TextOfTheRightLengthThatIsNotAMoveGetsTheMalformedMoveMessage()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var response = await client.PostMoveAsync(game.Id, "zz99");

        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors[nameof(PlayMoveRequest.Uci)].Should().Equal(GameEndpoints.MalformedMoveMessage);
    }

    // Whatever the move: one that would have been legal, the mating move
    // again, and text that is not a move at all.
    [Theory]
    [InlineData("a2a3")]
    [InlineData("d8h4")]
    [InlineData("zz99")]
    public async Task AMoveAfterTheGameHasEndedIsRefusedWith409AndSavesNothing(string uci)
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();
        var mated = await client.PlayAsync(game.Id, GameRequests.FoolsMate);

        using var response = await client.PostMoveAsync(game.Id, uci);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameFinishedDetail);
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(mated, InStrictOrder);
    }

    // The second of two identical moves sent one after the other, as a
    // double click sends them: by then the piece has left its square.
    [Fact]
    public async Task AMoveSentTwiceInARowIsSavedOnceAndRefusedOnce()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var first = await client.PostMoveAsync(game.Id, "e2e4");
        using var second = await client.PostMoveAsync(game.Id, "e2e4");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.ReadGameAsync(game.Id)).Moves.Should().Equal(new MoveResponse(1, "e2e4", "e4"));
    }

    // The mating move sent twice: the first ends the game, so the second is
    // a move in a finished game.
    [Fact]
    public async Task AMatingMoveSentTwiceInARowIsSavedOnceAndTheSecondFindsTheGameOver()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();
        await client.PlayAsync(game.Id, GameRequests.FoolsMate[..3]);

        using var first = await client.PostMoveAsync(game.Id, "d8h4");
        using var second = await client.PostMoveAsync(game.Id, "d8h4");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameFinishedDetail);
        (await client.ReadGameAsync(game.Id)).Moves.Should().HaveCount(4);
    }

    [Fact]
    public async Task AnotherPlayersGameIsNotFoundJustLikeAGameThatDoesNotExist()
    {
        using var owner = await SignedInClientAsync();
        var game = await owner.CreateGameAsync();
        using var otherPlayer = await SignedInClientAsync();
        var unknownId = Guid.CreateVersion7();

        using var readOthers = await otherPlayer.GetGameAsync(game.Id);
        using var readUnknown = await otherPlayer.GetGameAsync(unknownId);
        using var moveInOthers = await otherPlayer.PostMoveAsync(game.Id, "e2e4");
        using var moveInUnknown = await otherPlayer.PostMoveAsync(unknownId, "e2e4");

        foreach (var response in new[] { readOthers, readUnknown, moveInOthers, moveInUnknown })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameNotFoundDetail);
        }

        (await readOthers.ComparableBodyAsync()).Should().Be(await readUnknown.ComparableBodyAsync());
        (await moveInOthers.ComparableBodyAsync()).Should().Be(await moveInUnknown.ComparableBodyAsync());
        (await owner.ReadGameAsync(game.Id)).Should().BeEquivalentTo(game, InStrictOrder);
    }

    // Validation runs before the game is looked up, so a move too long to be
    // one says nothing about whether the game exists.
    [Fact]
    public async Task AnOverlongMoveGetsTheSameAnswerWhetherOrNotTheGameIsThePlayers()
    {
        using var owner = await SignedInClientAsync();
        var game = await owner.CreateGameAsync();
        using var otherPlayer = await SignedInClientAsync();

        using var inOwnGame = await owner.PostMoveAsync(game.Id, "e2e4qq");
        using var inOthersGame = await otherPlayer.PostMoveAsync(game.Id, "e2e4qq");

        inOwnGame.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await inOthersGame.ComparableBodyAsync()).Should().Be(await inOwnGame.ComparableBodyAsync());
    }

    [Fact]
    public async Task SignedOutRequestsAre401AndChangeNothing()
    {
        using var owner = await SignedInClientAsync();
        var game = await owner.CreateGameAsync();
        using var signedOut = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var create = await signedOut.PostGameAsync();
        using var read = await signedOut.GetGameAsync(game.Id);
        using var move = await signedOut.PostMoveAsync(game.Id, "e2e4");

        foreach (var response in new[] { create, read, move })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.Location.Should().BeNull();
            await response.ReadProblemAsync();
        }

        (await owner.ReadGameAsync(game.Id)).Should().BeEquivalentTo(game, InStrictOrder);
    }

    [Fact]
    public async Task SigningOutEndsAccessToThePlayersGames()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();
        using (var signOut = await client.SignOutAsync())
        {
            signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var read = await client.GetGameAsync(game.Id);

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The refusal of cross-site requests applies to every POST, so a page on
    // another site cannot play a move in the player's game with their cookie.
    [Fact]
    public async Task AMoveStartedByAnotherSiteIsRefusedAndSavesNothing()
    {
        using var client = await SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var forged = new HttpRequestMessage(HttpMethod.Post, $"/games/{game.Id}/moves")
        {
            Content = JsonContent.Create(new { uci = "e2e4" }),
        };
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        forged.Headers.TryAddWithoutValidation(HeaderNames.Origin, "https://attacker.example").Should().BeTrue();
        using var response = await client.SendAsync(forged, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Detail.Should().Be(CrossSiteRequests.RefusedDetail);
        (await client.ReadGameAsync(game.Id)).Moves.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRefusesTheComputerOpponentUnderTheOpponentKindField()
    {
        using var client = await SignedInClientAsync();

        using var response = await client.PostGameAsync(new { playerSide = "white", opponent = new { kind = "computer" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal("Opponent.Kind");
        problem.Errors["Opponent.Kind"].Should().Equal(GameEndpoints.UnofferedOpponentMessage);
    }

    public static TheoryData<string, string[]> MissingFields => new()
    {
        { "{}", ["PlayerSide", "Opponent"] },
        { """{"playerSide":null,"opponent":null}""", ["PlayerSide", "Opponent"] },
        { """{"playerSide":"white","opponent":{}}""", ["Opponent.Kind"] },
        { """{"playerSide":"white","opponent":{"kind":null}}""", ["Opponent.Kind"] },
    };

    [Theory]
    [MemberData(nameof(MissingFields))]
    public async Task CreateReportsMissingFieldsByName(string body, string[] fields)
    {
        using var client = await SignedInClientAsync();

        using var response = await client.PostAsync("/games", JsonBody(body), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().BeEquivalentTo(fields);
    }

    // Enumerations are read by name only; a number, even one that names a
    // member, is refused, as is a name that is not one.
    [Theory]
    [InlineData("""{"playerSide":"purple","opponent":{"kind":"none"}}""")]
    [InlineData("""{"playerSide":0,"opponent":{"kind":"none"}}""")]
    [InlineData("""{"playerSide":"white","opponent":{"kind":0}}""")]
    [InlineData("""{"playerSide":"white","opponent":"none"}""")]
    public async Task CreateRefusesValuesThatAreNotNamesOfTheirEnumeration(string body)
    {
        using var client = await SignedInClientAsync();

        using var response = await client.PostAsync("/games", JsonBody(body), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await response.ReadProblemAsync();
    }

    // Moves written straight to the table, as something other than the
    // store might write them, each leaving a game that cannot be played on.
    public static TheoryData<string, int, string> BrokenSavedGames => new()
    {
        {
            """INSERT INTO "Moves" ("GameId", "HalfMoveNumber", "Uci", "San") VALUES (@id, 1, 'e2e5', 'e5')""",
            1,
            "half-move 1 is illegal"
        },
        {
            """INSERT INTO "Moves" ("GameId", "HalfMoveNumber", "Uci", "San") VALUES (@id, 2, 'e2e4', 'e4')""",
            1,
            "its half-move numbers have a gap"
        },
        {
            // Fool's mate, with the game's status left as ongoing.
            """
            INSERT INTO "Moves" ("GameId", "HalfMoveNumber", "Uci", "San")
            VALUES (@id, 1, 'f2f3', 'f3'), (@id, 2, 'e7e5', 'e5'), (@id, 3, 'g2g4', 'g4'), (@id, 4, 'd8h4', 'Qh4#')
            """,
            4,
            "it is saved as ongoing, but its moves end it"
        },
    };

    [Theory]
    [MemberData(nameof(BrokenSavedGames))]
    public async Task ABrokenSavedGameAnswers500AndIsLoggedWithItsIdAndLeftAsItIs(string sql, int rows, string reason)
    {
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(host => host.ConfigureLogging(logging => logging
            .AddProvider(logs)
            .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = logged.CreateClient();
        var email = AccountData.NewEmail();
        await client.RegisterNewAsync(email);
        var game = await client.CreateGameAsync();
        (await postgres.ExecuteAsync(sql, game.Id)).Should().Be(rows);
        var ownerId = await factory.AccountIdAsync(email);
        var before = await factory.LoadGameAsync(ownerId, game.Id);

        using var read = await client.GetGameAsync(game.Id);
        using var move = await client.PostMoveAsync(game.Id, "e2e4");

        foreach (var response in new[] { read, move })
        {
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.ReadProblemAsync()).Detail.Should().BeNull();
        }

        logs.Messages.Where(message => message.StartsWith("Saved game", StringComparison.Ordinal))
            .Should().Equal(Enumerable.Repeat($"Saved game {game.Id} cannot be rebuilt: {reason}.", 2));
        (await factory.LoadGameAsync(ownerId, game.Id)).Should().BeEquivalentTo(before);
    }

    // The id must be a GUID for the route to match at all, so anything else
    // is an unknown path, signed in or not, and says nothing about any game.
    [Fact]
    public async Task AnIdThatIsNotAGuidIsAnUnknownPath()
    {
        using var client = await SignedInClientAsync();

        using var read = await client.GetAsync("/games/not-a-guid", Cancellation);
        using var move = await client.PostAsJsonAsync("/games/not-a-guid/moves", new { uci = "e2e4" }, Cancellation);

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        move.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static readonly Func<AwesomeAssertions.Equivalency.EquivalencyOptions<GameResponse>,
        AwesomeAssertions.Equivalency.EquivalencyOptions<GameResponse>> InStrictOrder =
        options => options.WithStrictOrdering();

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        return client;
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
