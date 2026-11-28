using System.Net;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Security;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.Net.Http.Headers;
using static ChessAscent.Api.Tests.Features.Games.GameRequests;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// POST /games/{id}/resign, through the API. Each test's HttpClient keeps the
// cookies the API sets, as a browser would, and every game a test reads
// belongs to an account the test registered itself.
public sealed class ResignEndpointTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // After 1. e4 it is Black's turn, so White resigns on the other side's
    // turn and Black on its own.
    [Theory]
    [InlineData("white", GameResult.BlackWins)]
    [InlineData("black", GameResult.WhiteWins)]
    public async Task ResigningEndsTheGameAsAWinForTheOtherSideWhoeverIsToMove(string playerSide, GameResult result)
    {
        using var client = await factory.SignedInClientAsync();
        var game = await client.CreateGameAsync(playerSide);
        var before = await client.PlayAsync(game.Id, "e2e4");

        using var response = await client.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resigned = await response.ReadGameAsync();
        // The position, the side to move and the moves stay as they were,
        // and the resignation is the game's last activity.
        resigned.Should().BeEquivalentTo(
            before with
            {
                Status = GameProgress.Finished,
                Result = result,
                EndReason = GameEndReason.Resignation,
                LegalMoves = new Dictionary<string, IReadOnlyList<string>>(),
                LastActivityAt = resigned.LastActivityAt,
            },
            InStrictOrder);
        resigned.LastActivityAt.Should().BeAfter(before.LastActivityAt);
        (await client.ReadGameAsync(game.Id)).Should().BeEquivalentTo(resigned, InStrictOrder);
    }

    [Fact]
    public async Task AGameWithNoMovesCanBeResigned()
    {
        using var client = await factory.SignedInClientAsync();
        var game = await client.CreateGameAsync();

        using var response = await client.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resigned = await response.ReadGameAsync();
        resigned.Result.Should().Be(GameResult.BlackWins);
        resigned.EndReason.Should().Be(GameEndReason.Resignation);
        resigned.Moves.Should().BeEmpty();
    }

    // As Black against the computer, which made the first move when the
    // game was created, after a hint: resigning asks neither engine for
    // anything, and leaves the moves and the hint count as they were.
    [Fact]
    public async Task ResigningAGameAgainstTheComputerGivesTheComputerTheWinAndKeepsTheHintCount()
    {
        var opponent = FakeOpponent.Playing("e2e4");
        var hints = FakeHintProvider.Hinting("e7e5");
        using var api = factory.WithFakes(opponent, hints);
        using var client = api.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateComputerGameAsync("black");
        await client.HintAsync(game);
        var before = await client.ReadGameAsync(game.Id);

        using var response = await client.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resigned = await response.ReadGameAsync();
        before.HintCount.Should().Be(1);
        before.Moves.Notation().Should().Equal((1, "e2e4", "e4"));
        resigned.Should().BeEquivalentTo(
            before with
            {
                Status = GameProgress.Finished,
                Result = GameResult.WhiteWins,
                EndReason = GameEndReason.Resignation,
                LegalMoves = new Dictionary<string, IReadOnlyList<string>>(),
                Promotions = [],
                LastActivityAt = resigned.LastActivityAt,
            },
            InStrictOrder);
        resigned.LastActivityAt.Should().BeAfter(before.LastActivityAt);
        opponent.Calls.Should().HaveCount(1);
        hints.Calls.Should().HaveCount(1);
    }

    // A game that ended by checkmate, and one already resigned, as when the
    // confirmation is pressed again or in a second tab.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFinishedGameCannotBeResignedAndIsLeftAsItIs(bool mated)
    {
        using var client = await factory.SignedInClientAsync();
        var game = await client.CreateGameAsync();
        if (mated)
        {
            await client.PlayAsync(game.Id, GameRequests.FoolsMate);
        }
        else
        {
            using var first = await client.PostResignAsync(game.Id);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var saved = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameFinishedDetail);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    // The other tab of the two-tab case: its move reaches a game that is over.
    [Fact]
    public async Task AMoveAfterTheResignationIsRefusedAsInAFinishedGame()
    {
        using var client = await factory.SignedInClientAsync();
        var game = await client.CreateGameAsync();
        await client.PlayAsync(game.Id, "e2e4");
        using (var resign = await client.PostResignAsync(game.Id))
        {
            resign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var move = await client.PostMoveAsync(game.Id, "e7e5", 2);

        move.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await move.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameFinishedDetail);
        (await client.ReadGameAsync(game.Id)).Moves.Notation().Should().Equal((1, "e2e4", "e4"));
    }

    [Fact]
    public async Task AnotherPlayersGameIsNotFoundJustLikeAGameThatDoesNotExist()
    {
        using var owner = await factory.SignedInClientAsync();
        var game = await owner.CreateGameAsync();
        using var otherPlayer = await factory.SignedInClientAsync();
        var saved = await postgres.SavedRowsAsync(game.Id);

        using var resignOthers = await otherPlayer.PostResignAsync(game.Id);
        using var resignUnknown = await otherPlayer.PostResignAsync(Guid.CreateVersion7());

        foreach (var response in new[] { resignOthers, resignUnknown })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.ReadProblemAsync()).Detail.Should().Be(GameEndpoints.GameNotFoundDetail);
        }

        (await resignOthers.ComparableBodyAsync()).Should().Be(await resignUnknown.ComparableBodyAsync());
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    [Fact]
    public async Task ASignedOutResignationIs401AndChangesNothing()
    {
        using var owner = await factory.SignedInClientAsync();
        var game = await owner.CreateGameAsync();
        using var signedOut = factory.CreateClient(new() { AllowAutoRedirect = false });
        var saved = await postgres.SavedRowsAsync(game.Id);

        using var response = await signedOut.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        await response.ReadProblemAsync();
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    // The refusal of cross-site requests applies to every POST, so a page on
    // another site cannot resign the player's game with their cookie.
    [Fact]
    public async Task AResignationStartedByAnotherSiteIsRefusedAndChangesNothing()
    {
        using var client = await factory.SignedInClientAsync();
        var game = await client.CreateGameAsync();
        var saved = await postgres.SavedRowsAsync(game.Id);

        using var forged = new HttpRequestMessage(HttpMethod.Post, $"/games/{game.Id}/resign");
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        forged.Headers.TryAddWithoutValidation(HeaderNames.Origin, "https://attacker.example").Should().BeTrue();
        using var response = await client.SendAsync(forged, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Detail.Should().Be(CrossSiteRequests.RefusedDetail);
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }

    // Each game the move endpoint's tests break by writing moves straight to
    // the table is refused with 500, and logged, before the resignation is
    // saved.
    [Theory]
    [MemberData(nameof(GameEndpointsTests.BrokenSavedGames), MemberType = typeof(GameEndpointsTests))]
    public async Task AnOngoingGameWhoseSavedFormIsBrokenAnswers500AndIsNotResigned(string sql, int rows, string reason)
    {
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithFakes(new FakeOpponent(), logs: logs);
        using var client = logged.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateGameAsync();
        (await postgres.ExecuteAsync(sql, game.Id)).Should().Be(rows);
        var saved = await postgres.SavedRowsAsync(game.Id);

        using var response = await client.PostResignAsync(game.Id);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.ReadProblemAsync()).Detail.Should().BeNull();
        logs.Messages.Where(message => message.StartsWith("Saved game", StringComparison.Ordinal))
            .Should().Equal($"Saved game {game.Id} cannot be rebuilt: {reason}.");
        (await postgres.SavedRowsAsync(game.Id)).Should().Be(saved);
    }
}
