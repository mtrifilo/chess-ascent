using System.Security.Claims;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ChessAscent.Api.Features.Games;

/// <summary>
/// Creating a game, reading it, and playing a move in it, for the signed-in
/// player's own games only.
/// </summary>
/// <remarks>
/// <para>
/// The answers a move can get, so the game screen can react to each:
/// 200 with the game after the move; 400 with a validation problem under
/// <c>Uci</c> when the text is missing, longer than
/// <see cref="GameLimits.UciLength"/> or not a UCI move; 404 when the
/// player has no game with that id; 409 with <see cref="GameFinishedDetail"/>
/// when the game is over; 409 with <see cref="MoveConflictDetail"/> when
/// another request saved a move first; 422 when the move is not legal in the
/// position. In every case but 200 nothing is saved.
/// </para>
/// <para>
/// Both 409s mean the game is no longer where the request found it, and the
/// screen answers both by loading the game again. A move sent twice, as a
/// double click does, gets one of three answers the second time, depending on
/// timing: 409 with the conflict detail when both were played on the same
/// position; 422 when the first had already been saved, since its piece has
/// left the square; or 409 with the finished detail when the first ended the
/// game.
/// </para>
/// </remarks>
public static class GameEndpoints
{
    /// <summary>The detail of the 404 for a game that does not exist or belongs to another player.</summary>
    public const string GameNotFoundDetail = "There is no game with this id.";

    /// <summary>The detail of the 422 for a well-formed move that is not legal in the position.</summary>
    public const string IllegalMoveDetail = "That move is not legal in this position.";

    /// <summary>The detail of the 409 for a move in a game that is over.</summary>
    public const string GameFinishedDetail = "The game is over, so no more moves can be played.";

    /// <summary>The detail of the 409 for a move that another request's move got to first.</summary>
    public const string MoveConflictDetail =
        "Another move was saved while this one was being played, so this one was not saved. Load the game to see where it stands.";

    /// <summary>The validation message for an opponent this API does not offer.</summary>
    public const string UnofferedOpponentMessage = "The only opponent offered is none: the player moves both sides.";

    /// <summary>The validation message for text that is not a move in UCI form.</summary>
    public const string MalformedMoveMessage = "Not a move in UCI form, such as e2e4 or e7e8q.";

    // The keys validation reports errors under: each property's path in C#.
    private const string OpponentKindField = nameof(CreateGameRequest.Opponent) + "." + nameof(OpponentRequest.Kind);
    private const string UciField = nameof(PlayMoveRequest.Uci);

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // LEARN: Settings made on a route group apply to every endpoint
        // mapped on it, now or later. RequireAuthorization here does what
        // .requestMatchers("/games/**").authenticated() does in a Spring
        // SecurityFilterChain: no endpoint below runs for a caller who is not
        // signed in, and one added later cannot forget the rule. The
        // authorization middleware answers 401 before the handler or any
        // filter runs. AddEndpointFilter wraps each endpoint's handler in
        // SavedGameFaultFilter, and ProducesProblem adds the 401 to each
        // endpoint's entry in the OpenAPI document.
        var games = endpoints.MapGroup("/games")
            .RequireAuthorization()
            .AddEndpointFilter<SavedGameFaultFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // The document learns 200, 201 and 400 from each handler's return
        // type. The rest are declared here: ProblemHttpResult carries its
        // status as a value, so its type says nothing about which statuses
        // a handler uses. 403 is the refusal of cross-site requests, which
        // comes from middleware (see CrossSiteRequests) and applies to every
        // POST.
        games.MapPost("", CreateAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // LEARN: {id:guid} is a route constraint: the route matches only when
        // that segment is a GUID, so /games/abc is an unknown path (404)
        // rather than a request whose id failed to bind (400). Spring
        // MVC would need a regular expression, {id:[0-9a-f-]+}. The handler's
        // parameter of the same name receives the parsed Guid.
        games.MapGet("/{id:guid}", GetAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        games.MapPost("/{id:guid}/moves", PlayMoveAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    /// <summary>Starts a game for the signed-in player from the standard starting position.</summary>
    // LEARN: Results<Created<GameResponse>, ValidationProblem> says this
    // handler returns exactly one of those two results. It is a generic
    // struct with an implicit conversion from each of its type arguments, so
    // each `return` hands back a plain Created or ValidationProblem and the
    // compiler wraps it, while returning any other result type is a compile
    // error. Spring's ResponseEntity<?> would accept anything and say nothing.
    // Each type argument also describes its own response to the OpenAPI
    // generator (201 with a GameResponse, 400 with validation errors), so the
    // signature is the documentation.
    private static async Task<Results<Created<GameResponse>, ValidationProblem>> CreateAsync(
        CreateGameRequest request,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        CancellationToken cancellationToken)
    {
        if (request.Opponent.Kind is not OpponentKind.None)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [OpponentKindField] = [UnofferedOpponentMessage],
            });
        }

        var board = rules.NewGame();

        // Validation has refused a missing side, so PlayerSide has a value.
        var game = await store.CreateAsync(
            PlayerId(principal, users), request.PlayerSide!.Value, Opponent.None, board.Fen, cancellationToken);
        return TypedResults.Created($"/games/{game.Id}", GameMapping.ToResponse(game, board));
    }

    /// <summary>One of the signed-in player's games.</summary>
    private static async Task<Results<Ok<GameResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        CancellationToken cancellationToken)
    {
        var game = await store.LoadAsync(PlayerId(principal, users), id, cancellationToken);
        return game is null
            ? NotFoundProblem()
            : TypedResults.Ok(GameMapping.ToResponse(game, Rebuild(rules, game)));
    }

    /// <summary>Plays one move in one of the signed-in player's games, and answers with the game after it.</summary>
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, ProblemHttpResult>> PlayMoveAsync(
        Guid id,
        PlayMoveRequest request,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        CancellationToken cancellationToken)
    {
        var playerId = PlayerId(principal, users);
        var game = await store.LoadAsync(playerId, id, cancellationToken);
        if (game is null)
        {
            return NotFoundProblem();
        }

        // The saved status decides, because a game can end in ways the
        // rebuilt board does not see, such as a resignation.
        if (game.Status == GameProgress.Finished)
        {
            return FinishedProblem();
        }

        var board = Rebuild(rules, game);
        return board.TryPlay(request.Uci) switch
        {
            MovePlayed played => await SaveAsync(store, playerId, game, board, played, cancellationToken),
            MalformedMove => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [UciField] = [MalformedMoveMessage],
            }),
            IllegalMove => TypedResults.Problem(IllegalMoveDetail, statusCode: StatusCodes.Status422UnprocessableEntity),
            GameAlreadyOver => FinishedProblem(),
        };
    }

    // Saves the move with the status it led to, in one transaction, and
    // answers with the game as saved, built from what this request played
    // rather than read back, so a move another request saves just after
    // this one does not appear in this answer.
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        GameStore store,
        string playerId,
        SavedGame game,
        IChessGame board,
        MovePlayed played,
        CancellationToken cancellationToken)
    {
        var appended = await store.AppendMovesAsync(
            playerId, game.Id, game.NextHalfMoveNumber, [played.Move], played.Status, cancellationToken);
        return appended switch
        {
            MovesAppended saved => TypedResults.Ok(GameMapping.ToResponse(WithMove(game, played, saved), board)),
            GameNotFound => NotFoundProblem(),
            GameFinished => FinishedProblem(),
            HalfMoveConflict => TypedResults.Problem(MoveConflictDetail, statusCode: StatusCodes.Status409Conflict),
        };
    }

    // The game as the store has just saved it: with the move, and with the
    // status the move led to, stored the way the store stores it.
    private static SavedGame WithMove(SavedGame game, MovePlayed played, MovesAppended saved)
    {
        var (progress, result, endReason) = GameStore.ToStoredOutcome(played.Status);
        return game with
        {
            Status = progress,
            Result = result,
            EndReason = endReason,
            UpdatedAt = saved.UpdatedAt,
            Moves = [.. game.Moves, played.Move],
        };
    }

    // Load replays every saved move through the same checks as live play, so
    // a refusal means the stored game is broken. The stored text is left
    // out of the reason, since nothing bounds what a broken row holds.
    private static IChessGame Rebuild(IChessRules rules, SavedGame game) =>
        rules.Load(game.StartingFen, [.. game.Moves.Select(move => move.Uci)]) switch
        {
            GameLoaded loaded => loaded.Game,
            InvalidStartingPosition => throw new UnrebuildableGameException(
                game.Id, "the starting position is not a FEN the chess interface accepts"),
            InvalidMoveInHistory invalid => throw new UnrebuildableGameException(
                game.Id, $"half-move {invalid.Index + 1} is {Describe(invalid.Rejection)}"),
        };

    private static string Describe(MoveRejection rejection) => rejection switch
    {
        IllegalMove => "illegal",
        MalformedMove => "not a UCI move",
        GameAlreadyOver => "after the end of the game",
    };

    // The group requires a signed-in player, and the session cookie Identity
    // issues always carries the account id, so a missing one is a bug here
    // rather than something to answer.
    private static string PlayerId(ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
        users.GetUserId(principal)
        ?? throw new InvalidOperationException("The signed-in player's session carries no account id.");

    private static ProblemHttpResult NotFoundProblem() =>
        TypedResults.Problem(GameNotFoundDetail, statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult FinishedProblem() =>
        TypedResults.Problem(GameFinishedDetail, statusCode: StatusCodes.Status409Conflict);
}
