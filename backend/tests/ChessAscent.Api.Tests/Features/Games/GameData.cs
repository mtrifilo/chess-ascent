using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AccountData = ChessAscent.Api.Tests.Features.Accounts.Accounts;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>Accounts, games and moves for the game store's tests.</summary>
/// <remarks>
/// Every test class shares one database and they run in parallel, so each
/// test creates owners of its own with <see cref="CreateOwnerAsync"/> and
/// only reads the games it created.
/// </remarks>
internal static class GameData
{
    public const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Holds no state, so one instance serves every test.
    public static readonly IChessRules Rules = new ChessRules();

    /// <summary>Creates an account no other test uses, and returns its id.</summary>
    public static async Task<string> CreateOwnerAsync(this ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = AccountData.NewEmail();
        var user = new IdentityUser { UserName = email, Email = email };

        // No password: the owners never sign in, and hashing one is slow.
        var created = await users.CreateAsync(user);

        created.Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>
    /// Creates a game, from the standard starting position unless
    /// <paramref name="startingFen"/> names another, on a store of its own.
    /// </summary>
    public static Task<SavedGame> CreateGameAsync(
        this ApiFactory factory,
        string ownerId,
        Opponent? opponent = null,
        Side playerSide = Side.White,
        string startingFen = StartingFen) =>
        factory.WithStoreAsync(store => store.CreateAsync(
            ownerId, playerSide, opponent ?? Opponent.None, startingFen, TestContext.Current.CancellationToken));

    /// <summary>
    /// Runs <paramref name="sql"/> on the database directly, bypassing the
    /// store, with <c>@id</c> bound to <paramref name="gameId"/>, and returns
    /// the number of rows it wrote.
    /// </summary>
    public static async Task<int> ExecuteAsync(this PostgresFixture postgres, string sql, Guid gameId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", gameId);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Loads a game, on a store of its own.</summary>
    public static Task<SavedGame?> LoadGameAsync(this ApiFactory factory, string ownerId, Guid gameId) =>
        factory.WithStoreAsync(store => store.LoadAsync(ownerId, gameId, TestContext.Current.CancellationToken));

    /// <summary>Appends moves to a game, on a store of its own.</summary>
    public static Task<AppendResult> AppendAsync(
        this ApiFactory factory,
        string ownerId,
        Guid gameId,
        int firstHalfMoveNumber,
        IReadOnlyList<PlayedMove> moves,
        GameStatus statusAfter) =>
        factory.WithStoreAsync(store => store.AppendMovesAsync(
            ownerId, gameId, firstHalfMoveNumber, moves, statusAfter, TestContext.Current.CancellationToken));

    /// <summary>Runs <paramref name="action"/> on a store of its own, as one request would.</summary>
    public static async Task<T> WithStoreAsync<T>(this ApiFactory factory, Func<GameStore, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GameStore>());
    }

    /// <summary>
    /// Plays the moves on <paramref name="game"/> through the chess interface
    /// and returns them as it recorded them, with the status after the last.
    /// </summary>
    public static (IReadOnlyList<PlayedMove> Moves, GameStatus Status) Play(this IChessGame game, params string[] uciMoves)
    {
        var played = new List<PlayedMove>();
        foreach (var uci in uciMoves)
        {
            var result = game.TryPlay(uci);
            if (result is not MovePlayed move)
            {
                throw new InvalidOperationException($"{uci} was refused in {game.Fen}: {result}.");
            }

            played.Add(move.Move);
        }

        return (played, game.Status);
    }

    /// <summary>The game rebuilt from what was saved, through the chess interface.</summary>
    public static IChessGame Rebuild(this SavedGame saved) =>
        Rules.Load(saved.StartingFen, [.. saved.Moves.Select(move => move.Uci)]) switch
        {
            GameLoaded loaded => loaded.Game,
            var other => throw new InvalidOperationException($"The saved game did not load: {other}."),
        };
}
