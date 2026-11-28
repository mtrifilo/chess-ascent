using System.Net;
using System.Text.Json;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

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

    /// <summary>The half-move number the game's next move has.</summary>
    public static int NextHalfMoveNumber(this GameResponse game) => game.Moves.Count + 1;

    /// <summary>Reads a game body with the serializer settings the API writes it with.</summary>
    public static async Task<GameResponse> ReadGameAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var game = await response.Content.ReadFromJsonAsync<GameResponse>(ApiJson, Cancellation);
        return game!;
    }

    /// <summary>The account id of the player signed in under <paramref name="email"/>.</summary>
    public static async Task<string> AccountIdAsync(this ApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }
}
