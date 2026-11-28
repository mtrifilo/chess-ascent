using System.Net;
using System.Text.Json.Nodes;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Tests.Features.Accounts;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

// Every test lists the games of accounts it registered itself, so the games
// other test classes write to the shared database never appear in its lists.
public sealed class GameListTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    private const string Ongoing = "ongoing";
    private const string Finished = "finished";

    // PostgreSQL orders uuids byte by byte, which is the ordinal order of
    // their text form.
    private static readonly IComparer<Guid> DatabaseOrder =
        Comparer<Guid>.Create((left, right) => string.CompareOrdinal(left.ToString(), right.ToString()));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheListHoldsOnlyTheCallersGames()
    {
        using var player = await factory.SignedInClientAsync();
        using var otherPlayer = await factory.SignedInClientAsync();
        var first = await player.CreateGameAsync();
        var second = await player.CreateGameAsync("black");
        var others = await otherPlayer.CreateGameAsync();

        var players = await player.ListGamesAsync(Ongoing);
        var otherPlayers = await otherPlayer.ListGamesAsync(Ongoing);

        players.Games.Select(game => game.Id).Should().Equal(second.Id, first.Id);
        otherPlayers.Games.Select(game => game.Id).Should().Equal(others.Id);
    }

    // The cursor only says where a page starts, so another player's cursor,
    // which falls among the caller's games by time, still lists only the
    // caller's own.
    [Fact]
    public async Task AnotherPlayersCursorListsOnlyTheCallersGames()
    {
        using var player = await factory.SignedInClientAsync();
        using var otherPlayer = await factory.SignedInClientAsync();
        var players = await player.CreateGameAsync();
        await otherPlayer.CreateGameAsync();
        await otherPlayer.CreateGameAsync();
        await otherPlayer.CreateGameAsync();
        var othersFirstPage = await otherPlayer.ListGamesAsync(Ongoing, pageSize: 1);

        var page = await player.ListGamesAsync(Ongoing, cursor: othersFirstPage.NextCursor);

        othersFirstPage.NextCursor.Should().NotBeNull();
        page.Games.Select(game => game.Id).Should().Equal(players.Id);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GamesAreListedMostRecentActivityFirst()
    {
        using var client = await factory.SignedInClientAsync();
        var oldest = await client.CreateGameAsync();
        var middle = await client.CreateGameAsync();
        var newest = await client.CreateGameAsync();
        await client.PlayAsync(oldest.Id, "e2e4");

        var page = await client.ListGamesAsync(Ongoing);

        page.Games.Select(game => game.Id).Should().Equal(oldest.Id, newest.Id, middle.Id);
        page.Games.Select(game => game.LastActivityAt).Should().BeInDescendingOrder();
        page.Games.Select(game => game.HalfMoveCount).Should().Equal(1, 0, 0);
        page.NextCursor.Should().BeNull();
    }

    // Games saved in the same instant are ordered by id, in the database's
    // order of ids, on every request and across a page boundary.
    [Fact]
    public async Task GamesWithTheSameLastActivityAreInTheSameOrderOnEveryRequestAndEveryPage()
    {
        using var client = await factory.SignedInClientAsync();
        var ids = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var game = await client.CreateGameAsync();
            (await postgres.ExecuteAsync(
                """UPDATE "Games" SET "UpdatedAt" = '2026-11-01T12:00:00Z' WHERE "Id" = @id""", game.Id)).Should().Be(1);
            ids.Add(game.Id);
        }

        var first = await client.ListGamesAsync(Ongoing);
        var second = await client.ListGamesAsync(Ongoing);
        var paged = await client.ListEveryPageAsync(Ongoing, pageSize: 1);

        var expected = ids.Order(DatabaseOrder).Reverse().ToList();
        first.Games.Select(game => game.Id).Should().Equal(expected);
        second.Games.Select(game => game.Id).Should().Equal(expected);
        paged.SelectMany(page => page.Games).Select(game => game.Id).Should().Equal(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task PagesTogetherHoldEveryGameOnceInListOrder(int pageSize)
    {
        using var client = await factory.SignedInClientAsync();
        var created = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            created.Add((await client.CreateGameAsync()).Id);
        }

        var pages = await client.ListEveryPageAsync(Ongoing, pageSize);

        pages.SelectMany(page => page.Games).Select(game => game.Id).Should().Equal(Enumerable.Reverse(created));
        pages.Should().HaveCount((created.Count + pageSize - 1) / pageSize);
        pages[..^1].Should().AllSatisfy(page =>
        {
            page.Games.Should().HaveCount(pageSize);
            page.NextCursor.Should().NotBeNull();
        });
        pages[^1].NextCursor.Should().BeNull();
    }

    // The next page starts after the previous page's last game as the list
    // stands when it is asked for. A game that has had a move since is now
    // at the top, above where the next page starts, so the next page does
    // not hold it; every other game is listed exactly once.
    [Fact]
    public async Task AGameWithAMoveSinceThePreviousPageIsNotOnTheNextPage()
    {
        using var client = await factory.SignedInClientAsync();
        var oldest = await client.CreateGameAsync();
        var older = await client.CreateGameAsync();
        var newer = await client.CreateGameAsync();
        var newest = await client.CreateGameAsync();

        var firstPage = await client.ListGamesAsync(Ongoing, pageSize: 2);
        await client.PlayAsync(newer.Id, "e2e4");
        await client.PlayAsync(oldest.Id, "e2e4");
        var secondPage = await client.ListGamesAsync(Ongoing, pageSize: 2, firstPage.NextCursor);
        var fromTheTop = await client.ListGamesAsync(Ongoing);

        firstPage.Games.Select(game => game.Id).Should().Equal(newest.Id, newer.Id);
        secondPage.Games.Select(game => game.Id).Should().Equal(older.Id);
        secondPage.NextCursor.Should().BeNull();
        fromTheTop.Games.Select(game => game.Id).Should().Equal(oldest.Id, newer.Id, newest.Id, older.Id);
    }

    [Fact]
    public async Task EachStatusListsOnlyTheGamesWithThatStatus()
    {
        using var client = await factory.SignedInClientAsync();
        var ongoing = await client.CreateGameAsync();
        var mated = await client.CreateGameAsync();
        await client.PlayAsync(mated.Id, GameRequests.FoolsMate);

        var ongoingPage = await client.ListGamesAsync(Ongoing);
        var finishedPage = await client.ListGamesAsync(Finished);

        ongoingPage.Games.Should().ContainSingle().Which.Should().Be(new GameSummaryResponse(
            ongoing.Id,
            Side.White,
            new OpponentResponse(OpponentKind.None, null),
            GameProgress.Ongoing,
            null,
            null,
            0,
            ongoingPage.Games[0].LastActivityAt));
        finishedPage.Games.Should().ContainSingle().Which.Should().Be(new GameSummaryResponse(
            mated.Id,
            Side.White,
            new OpponentResponse(OpponentKind.None, null),
            GameProgress.Finished,
            GameResult.BlackWins,
            GameEndReason.Checkmate,
            4,
            finishedPage.Games[0].LastActivityAt));
    }

    [Theory]
    [InlineData(Ongoing)]
    [InlineData(Finished)]
    public async Task APlayerWithNoGamesGetsAnEmptyLastPage(string status)
    {
        using var client = await factory.SignedInClientAsync();

        var page = await client.ListGamesAsync(status);

        page.Games.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    // The wire format the front end's generated types describe: a computer
    // game's row, with the time as an ISO 8601 instant in UTC equal to the
    // saved one.
    [Fact]
    public async Task ARowIsWrittenWithCamelCaseNamesAndTheSavedTimeInUtc()
    {
        var player = await factory.SignedInPlayerAsync();
        using var client = player.Client;
        var saved = await factory.CreateGameAsync(player.OwnerId, Opponent.Computer(1200), Side.Black);

        using var response = await client.GetGameListAsync("?status=ongoing");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancellation))!.AsObject();

        body.Select(property => property.Key).Should().Equal("games", "nextCursor");
        body["nextCursor"].Should().BeNull();
        var row = body["games"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        row.Select(property => property.Key).Should().Equal(
            "id", "playerSide", "opponent", "status", "result", "endReason", "halfMoveCount", "lastActivityAt");
        row["id"]!.GetValue<string>().Should().Be(saved.Id.ToString());
        row["playerSide"]!.GetValue<string>().Should().Be("black");
        row["opponent"]!.ToJsonString().Should().Be("""{"kind":"computer","strength":1200}""");
        row["status"]!.GetValue<string>().Should().Be("ongoing");
        row["result"].Should().BeNull();
        row["endReason"].Should().BeNull();
        row["halfMoveCount"]!.GetValue<int>().Should().Be(0);
        var lastActivity = row["lastActivityAt"]!.GetValue<string>();
        lastActivity.Should().EndWith("+00:00");
        DateTimeOffset.Parse(lastActivity, System.Globalization.CultureInfo.InvariantCulture).Should().Be(saved.UpdatedAt);
    }

    [Fact]
    public async Task WithoutAPageSizeAPageHoldsTheDefaultNumberOfGames()
    {
        var player = await factory.SignedInPlayerAsync();
        using var client = player.Client;
        for (var index = 0; index <= GameEndpoints.DefaultPageSize; index++)
        {
            await factory.CreateGameAsync(player.OwnerId);
        }

        var page = await client.ListGamesAsync(Ongoing);

        page.Games.Should().HaveCount(GameEndpoints.DefaultPageSize);
        page.NextCursor.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(GameEndpoints.MaxPageSize, 2, false)]
    public async Task PageSizesAtTheBoundsAreAccepted(int pageSize, int listed, bool hasNext)
    {
        using var client = await factory.SignedInClientAsync();
        await client.CreateGameAsync();
        await client.CreateGameAsync();

        var page = await client.ListGamesAsync(Ongoing, pageSize);

        page.Games.Should().HaveCount(listed);
        (page.NextCursor is not null).Should().Be(hasNext);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("2147483647")]
    public async Task PageSizesBeyondTheBoundsAreRefused(string pageSize)
    {
        using var client = await factory.SignedInClientAsync();

        using var response = await client.GetGameListAsync($"?status=ongoing&pageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal("pageSize");
    }

    // A page size that is not a whole number cannot be bound to the
    // parameter, so it is refused before validation runs, with a plain
    // problem rather than one under pageSize.
    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task APageSizeThatIsNotAWholeNumberIsRefused(string pageSize)
    {
        using var client = await factory.SignedInClientAsync();

        using var response = await client.GetGameListAsync($"?status=ongoing&pageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await response.ReadProblemAsync();
    }

    // Only the names the game response writes select games; an unknown value
    // is refused rather than answered with an empty list.
    [Theory]
    [InlineData("")]
    [InlineData("?status=")]
    [InlineData("?status=Ongoing")]
    [InlineData("?status=ONGOING")]
    [InlineData("?status=0")]
    [InlineData("?status=all")]
    [InlineData("?status=ongoing,finished")]
    [InlineData("?status=%20ongoing")]
    public async Task AMissingOrUnknownStatusIsRefused(string query)
    {
        using var client = await factory.SignedInClientAsync();

        using var response = await client.GetGameListAsync(query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, string[]>("status", [GameEndpoints.UnknownStatusMessage]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    // Not base64url at all: a length no base64 text has, and a character
    // outside the alphabet.
    [InlineData("bogus")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA*")]
    // The right length, with a time before year 1.
    [InlineData("________________________________")]
    // One byte short, and three bytes over.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task ACursorTheListDidNotSendIsRefused(string cursor)
    {
        using var client = await factory.SignedInClientAsync();

        using var response = await client.GetGameListAsync($"?status=ongoing&cursor={Uri.EscapeDataString(cursor)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, string[]>("cursor", [GameEndpoints.MalformedCursorMessage]));
    }

    [Fact]
    public async Task SignedOutTheListIs401()
    {
        using var signedOut = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await signedOut.GetGameListAsync("?status=ongoing");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        await response.ReadProblemAsync();
    }

    // EF Core logs each command it sends, with its SQL, so the log shows
    // every round trip a request makes to the database.
    [Fact]
    public async Task AListRequestRunsOneQueryThatSelectsNoMoveText()
    {
        var logs = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(host => host.ConfigureLogging(logging => logging
            .AddProvider(logs)
            .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = logged.CreateClient();
        await client.RegisterNewAsync(AccountData.NewEmail());
        var game = await client.CreateGameAsync();
        await client.PlayAsync(game.Id, "e2e4", "e7e5");
        var before = logs.Messages.Count;

        var page = await client.ListGamesAsync(Ongoing, pageSize: 5);

        page.Games.Should().ContainSingle().Which.HalfMoveCount.Should().Be(2);
        var sql = logs.Messages.Skip(before)
            .Where(message => message.StartsWith("Executed DbCommand", StringComparison.Ordinal))
            .Should().ContainSingle().Subject;
        sql.Should().Contain("""FROM "Games" """).And.Contain("LIMIT");
        foreach (var unlisted in new[] { "\"Uci\"", "\"San\"", "\"StartingFen\"", "\"CreatedAt\"" })
        {
            sql.Should().NotContain(unlisted);
        }
    }
}
