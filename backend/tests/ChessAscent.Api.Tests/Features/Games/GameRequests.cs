using System.Net;
using System.Text.Json;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Json;
using ChessAscent.Api.Tests.Features.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>Calls to the game endpoints, sent the way the account tests send theirs.</summary>
/// <remarks>
/// The bodies are written as JSON by hand, in the API's wire names, so a test
/// sends what the front end would rather than what this API's own types
/// serialize to.
/// </remarks>
internal static class GameRequests
{
    // Fool's mate: Black mates on the fourth half-move.
    public static readonly string[] FoolsMate = ["f2f3", "e7e5", "g2g4", "d8h4"];

    // The serializer settings the API reads and writes bodies with, built by
    // the API's own registration.
    private static readonly JsonSerializerOptions ApiJson = new ServiceCollection()
        .AddJsonConventions()
        .BuildServiceProvider()
        .GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;

    /// <summary>For comparing two games with BeEquivalentTo, moves and squares in the same order.</summary>
    public static readonly Func<AwesomeAssertions.Equivalency.EquivalencyOptions<GameResponse>,
        AwesomeAssertions.Equivalency.EquivalencyOptions<GameResponse>> InStrictOrder =
        options => options.WithStrictOrdering();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A request body of JSON text, written by hand.</summary>
    public static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> PostGameAsync(this HttpClient client, object body) =>
        client.PostAsJsonAsync("/games", body, Cancellation);

    public static Task<HttpResponseMessage> PostGameAsync(this HttpClient client, string playerSide = "white") =>
        client.PostGameAsync(new { playerSide, opponent = new { kind = "none" } });

    public static Task<HttpResponseMessage> PostComputerGameAsync(this HttpClient client, string playerSide, int strength) =>
        client.PostGameAsync(new { playerSide, opponent = new { kind = "computer", strength } });

    public static Task<HttpResponseMessage> GetGameAsync(this HttpClient client, Guid gameId) =>
        client.GetAsync($"/games/{gameId}", Cancellation);

    public static Task<HttpResponseMessage> PostMoveAsync(this HttpClient client, Guid gameId, string? uci, int halfMoveNumber) =>
        client.PostAsJsonAsync($"/games/{gameId}/moves", new { uci, halfMoveNumber }, Cancellation);

    public static Task<HttpResponseMessage> PostHintAsync(this HttpClient client, Guid gameId, int halfMoveNumber) =>
        client.PostAsJsonAsync($"/games/{gameId}/hints", new { halfMoveNumber }, Cancellation);

    /// <summary>Asks for a hint for the game's next half-move, checking that it answered 200, and returns it.</summary>
    public static async Task<HintResponse> HintAsync(this HttpClient client, GameResponse game)
    {
        using var response = await client.PostHintAsync(game.Id, game.NextHalfMoveNumber());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadHintAsync();
    }

    /// <summary>Reads a hint body with the serializer settings the API writes it with.</summary>
    public static Task<HintResponse> ReadHintAsync(this HttpResponseMessage response) =>
        response.ReadBodyAsync<HintResponse>();

    /// <summary>Resigns the game, with no body, as the game screen sends it.</summary>
    public static Task<HttpResponseMessage> PostResignAsync(this HttpClient client, Guid gameId) =>
        client.PostAsync($"/games/{gameId}/resign", content: null, Cancellation);

    /// <summary>Creates a game, checking that it answered 201, and returns it.</summary>
    public static async Task<GameResponse> CreateGameAsync(this HttpClient client, string playerSide = "white")
    {
        using var response = await client.PostGameAsync(playerSide);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.ReadGameAsync();
    }

    /// <summary>Creates a game against the computer, checking that it answered 201, and returns it.</summary>
    public static async Task<GameResponse> CreateComputerGameAsync(this HttpClient client, string playerSide, int strength = 800)
    {
        using var response = await client.PostComputerGameAsync(playerSide, strength);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.ReadGameAsync();
    }

    /// <summary>Reads a game, checking that it answered 200.</summary>
    public static async Task<GameResponse> ReadGameAsync(this HttpClient client, Guid gameId)
    {
        using var response = await client.GetGameAsync(gameId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadGameAsync();
    }

    /// <summary>
    /// Plays each move in turn, each as the game's next half-move, checking
    /// that each answered 200, and returns the game after the last.
    /// </summary>
    public static async Task<GameResponse> PlayAsync(this HttpClient client, Guid gameId, params string[] uciMoves)
    {
        if (uciMoves.Length == 0)
        {
            throw new ArgumentException("No moves to play.", nameof(uciMoves));
        }

        var game = await client.ReadGameAsync(gameId);
        foreach (var uci in uciMoves)
        {
            using var response = await client.PostMoveAsync(gameId, uci, game.NextHalfMoveNumber());
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{uci} is legal here");
            game = await response.ReadGameAsync();
        }

        return game;
    }

    /// <summary>Asks for a page of the list of games, with the query string as given.</summary>
    public static Task<HttpResponseMessage> GetGameListAsync(this HttpClient client, string query) =>
        client.GetAsync($"/games{query}", Cancellation);

    /// <summary>Reads a page of the list of games, checking that it answered 200.</summary>
    public static async Task<GameListResponse> ListGamesAsync(
        this HttpClient client, string status, int? pageSize = null, string? cursor = null)
    {
        var query = $"?status={status}"
            + (pageSize is { } size ? $"&pageSize={size}" : "")
            + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var response = await client.GetGameListAsync(query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadBodyAsync<GameListResponse>();
    }

    /// <summary>
    /// Reads every page of the list in turn, following each page's cursor,
    /// and returns the pages.
    /// </summary>
    public static async Task<List<GameListResponse>> ListEveryPageAsync(this HttpClient client, string status, int pageSize)
    {
        var pages = new List<GameListResponse>();
        string? cursor = null;
        do
        {
            var page = await client.ListGamesAsync(status, pageSize, cursor);
            pages.Add(page);
            cursor = page.NextCursor;

            // A list that always had a next page would otherwise never end the test.
            pages.Should().HaveCountLessThan(100);
        }
        while (cursor is not null);

        return pages;
    }

    /// <summary>
    /// Each move's half-move number, UCI and SAN, leaving out the position
    /// after it, for a test about the moves rather than the positions.
    /// </summary>
    public static IEnumerable<(int HalfMoveNumber, string Uci, string San)> Notation(this IEnumerable<MoveResponse> moves) =>
        moves.Select(move => (move.HalfMoveNumber, move.Uci, move.San));

    /// <summary>The half-move number the game's next move has.</summary>
    public static int NextHalfMoveNumber(this GameResponse game) => game.Moves.Count + 1;

    /// <summary>Reads a game body with the serializer settings the API writes it with.</summary>
    public static Task<GameResponse> ReadGameAsync(this HttpResponseMessage response) =>
        response.ReadBodyAsync<GameResponse>();

    /// <summary>Reads a JSON body with the serializer settings the API writes it with.</summary>
    public static async Task<T> ReadBodyAsync<T>(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadFromJsonAsync<T>(ApiJson, Cancellation);
        return body!;
    }

    /// <summary>A browser signed in to a new account of its own.</summary>
    public static async Task<HttpClient> SignedInClientAsync(this WebApplicationFactory<Program> factory) =>
        (await factory.SignedInPlayerAsync()).Client;

    /// <summary>A browser signed in to a new account of its own, and the account's id.</summary>
    public static async Task<(HttpClient Client, string OwnerId)> SignedInPlayerAsync(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var email = AccountData.NewEmail();
        await client.RegisterNewAsync(email);
        return (client, await factory.AccountIdAsync(email));
    }

    /// <summary>The account id of the player signed in under <paramref name="email"/>.</summary>
    public static async Task<string> AccountIdAsync(this WebApplicationFactory<Program> factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }
}
