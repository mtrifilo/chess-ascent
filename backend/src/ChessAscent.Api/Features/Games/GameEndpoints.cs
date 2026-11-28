using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ChessAscent.Api.Json;
using ChessAscent.Api.Opponents;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ChessAscent.Api.Features.Games;

/// <summary>
/// Listing games, creating a game, reading it, and playing a move in it, for
/// the signed-in player's own games only. Against the computer, one request plays the
/// player's move and the computer's reply.
/// </summary>
/// <remarks>
/// <para>
/// The answers creating a game can get: 201 with the game, which already
/// holds the computer's first move when the player has Black; 400 with a
/// validation problem when a field is missing, under
/// <c>Opponent.Strength</c> when the strength is not one the computer offers
/// or is given for an opponent of kind <c>none</c>, and a plain 400 problem
/// when the body cannot be read; 401; 403 when a page on another origin
/// started the request; 500 when what the computer answered means the server
/// is at fault (see <see cref="ComputerGameFaultException"/>); and 503 with <see cref="OpponentUnavailableDetail"/> when
/// the computer did not make its first move. In every case but 201 no game
/// is created.
/// </para>
/// <para>
/// The answers a move can get, so the game screen can react to each:
/// 200 with the game after the move, and after the computer's reply when it
/// has one; 400 with a validation problem under <c>Uci</c> when the text is
/// missing, longer than <see cref="PlayMoveRequest.UciLength"/> or not a UCI
/// move, or under <c>HalfMoveNumber</c> when that is missing, and a plain 400
/// problem when the body is missing, is not valid JSON, or is JSON of another
/// shape; 415 when its content type is not JSON; 401 when nobody is
/// signed in; 403 when a page on another origin started the request (see
/// CrossSiteRequests); 404 when the player has no game with that id; 409 with
/// <see cref="GameFinishedDetail"/> when the game is over; 409 with
/// <see cref="MoveConflictDetail"/> when the half-move number is not the
/// game's next one, or another request saved a move first; 422 when the move
/// is not legal in the position; 500 when the game is still being played but
/// its saved form is broken (see SavedGameBoards.RebuildBoard), or it is a
/// computer game that cannot be played on, because of what is saved or of
/// what the computer answered (see <see cref="ComputerGameFaultException"/>);
/// and 503 with
/// <see cref="OpponentUnavailableDetail"/> when the computer did not reply.
/// In every case but 200 nothing is saved, the player's move included.
/// </para>
/// <para>
/// Both 409s mean the game is no longer where the request found it, and the
/// screen answers both by loading the game again. A 503 means nothing has
/// changed, and the screen may send the same move, with the same half-move
/// number, again. Sending a move again is always safe: either it was not
/// saved and is played now, or it was, and its half-move number is taken
/// (409 with the conflict detail) or the game has ended (409 with the
/// finished detail). That is also every answer a double click's second
/// request can get.
/// </para>
/// <para>
/// Reading a game answers 200, 401, 404, or 500 when its saved form is
/// broken (see SavedGameBoards.RebuildBoard), a finished game included.
/// </para>
/// <para>
/// Listing games answers 200 with a page of them, which is empty when there
/// are none; 400 with a validation problem when <c>status</c> is missing, or
/// under whichever of <c>status</c>, <c>pageSize</c> and <c>cursor</c> holds
/// a value the list does not accept (a <c>pageSize</c> that is not a whole
/// number that fits an int is also refused with 400, but in Development as
/// a plain problem); and 401.
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

    /// <summary>
    /// The detail of the 409 for a move whose half-move number is not the
    /// game's next one, or that another request's move got to first.
    /// </summary>
    public const string MoveConflictDetail =
        "This move was not saved: the game is not where the request expected it, because another move was saved first or the half-move number is not the game's next one. Load the game to see where it stands.";

    /// <summary>
    /// The detail of the 503 for a game the computer did not create, or a
    /// move it did not reply to. Nothing was saved, and the same request can
    /// be sent again.
    /// </summary>
    public const string OpponentUnavailableDetail =
        "The computer did not make its move, so nothing was saved. Send the same request again to try once more.";

    /// <summary>The validation message for a strength the computer does not play at.</summary>
    public static readonly string UnofferedStrengthMessage =
        $"The computer plays at one of these strengths: {string.Join(", ", OpponentStrengths.Offered)}.";

    /// <summary>The validation message for a strength given for an opponent of kind none.</summary>
    public const string StrengthWithoutComputerMessage =
        "Only the computer has a strength. Leave it out when the opponent is none.";

    /// <summary>The validation message for text that is not a move in UCI form.</summary>
    public const string MalformedMoveMessage = "Not a move in UCI form, such as e2e4 or e7e8q.";

    /// <summary>The number of games on a page of the list when the request does not say.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The most games a page of the list can hold; a larger page size is refused with 400.</summary>
    public const int MaxPageSize = 100;

    // The status names the list accepts: exactly the names the game response
    // writes, matched as they are written, so "Ongoing" and "0" are refused.
    // Declared before the message that lists them, because static fields are
    // initialized in the order they are written.
    private static readonly Dictionary<string, GameProgress> ListStatuses = Enum.GetValues<GameProgress>()
        .ToDictionary(JsonServices.WireName, StringComparer.Ordinal);

    /// <summary>The validation message for a list request with no status, or one that names no status.</summary>
    public static readonly string UnknownStatusMessage =
        $"Give the status of the games to list: {string.Join(" or ", ListStatuses.Keys)}.";

    /// <summary>The validation message for a cursor the API did not send.</summary>
    public const string MalformedCursorMessage =
        "Not a cursor from this list. Send a page's nextCursor back unchanged, or leave it out for the first page.";

    // The keys validation reports errors under: each property's path in C#.
    private const string OpponentStrengthField = nameof(CreateGameRequest.Opponent) + "." + nameof(OpponentRequest.Strength);
    private const string UciField = nameof(PlayMoveRequest.Uci);

    // The list's query parameters, which are also the keys of its validation errors.
    private const string StatusParameter = "status";
    private const string CursorParameter = "cursor";

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
        games.MapGet("", ListAsync)
            .AddOpenApiOperationTransformer(DescribeStatusAsGameProgress);
        games.MapPost("", CreateAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // LEARN: {id:guid} is a route constraint: the route matches only when
        // that segment parses as a GUID, so /games/abc is an unknown path
        // (404) rather than a request whose id failed to bind (400). In
        // Spring MVC, /games/{id} with a UUID parameter matches /games/abc and
        // then fails the conversion with 400; only a regular expression for
        // the whole UUID in the path pattern makes it a 404. The handler's
        // parameter of the same name receives the parsed Guid.
        games.MapGet("/{id:guid}", GetAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        games.MapPost("/{id:guid}/moves", PlayMoveAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// One page of the signed-in player's ongoing or finished games, most
    /// recent activity first.
    /// </summary>
    /// <remarks>
    /// The class remarks list its answers, and <see cref="GameListResponse.NextCursor"/>
    /// says what a page after a cursor holds.
    /// </remarks>
    // LEARN: A handler parameter of a simple type (a string, a number, an
    // enum) that is not part of the route is read from the query string, as
    // a Spring MVC @RequestParam is, and a default value makes it optional.
    // The [Range] attribute is checked by AddValidation (see Program.cs)
    // before the handler runs, which answers 400 with a validation problem
    // under "pageSize", much as @Min and @Max with @Validated do. The status
    // is read as a string rather than as GameProgress because the binder
    // matches an enum's C# name ("Ongoing"), not the camel-case name the API
    // writes, and accepts numbers too.
    private static async Task<Results<Ok<GameListResponse>, ValidationProblem>> ListAsync(
        string? status,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        CancellationToken cancellationToken,
        [Range(1, MaxPageSize)] int pageSize = DefaultPageSize,
        string? cursor = null)
    {
        if (status is null || !ListStatuses.TryGetValue(status, out var progress))
        {
            return FieldProblem(StatusParameter, UnknownStatusMessage);
        }

        GameListCursor? after = null;
        if (cursor is not null)
        {
            if (!GameListCursor.TryDecode(cursor, out var decoded))
            {
                return FieldProblem(CursorParameter, MalformedCursorMessage);
            }

            after = decoded;
        }

        var page = await store.ListAsync(PlayerId(principal, users), progress, after, pageSize, cancellationToken);
        return TypedResults.Ok(GameMapping.ToResponse(page));
    }

    // The status is bound as a string (see ListAsync), so the generator
    // describes it as any string. It is described instead as the GameProgress
    // schema the responses use, which gives the front end the same named type.
    private static Task DescribeStatusAsGameProgress(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var described = operation.Parameters?.SingleOrDefault(p => p.Name == StatusParameter)
            ?? throw new InvalidOperationException($"GET /games has no {StatusParameter} parameter to describe.");
        if (described is not OpenApiParameter parameter)
        {
            throw new InvalidOperationException(
                $"GET /games describes {StatusParameter} as a {described.GetType().Name}, which cannot be edited.");
        }

        parameter.Required = true;
        parameter.Schema = new OpenApiSchemaReference(nameof(GameProgress), context.Document);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a game for the signed-in player from the standard starting
    /// position, with the computer's first move already played when the
    /// player has Black.
    /// </summary>
    // LEARN: Results<Created<GameResponse>, ValidationProblem,
    // ProblemHttpResult> says this handler returns exactly one of those three
    // results. It is a sealed generic class with an implicit conversion from
    // each of its type arguments, so each `return` hands back a plain Created,
    // ValidationProblem or ProblemHttpResult and the compiler wraps it, while
    // returning any other result type is a compile error. Spring's
    // ResponseEntity<?> would accept anything and say nothing. Most type
    // arguments also describe their own response to the OpenAPI generator
    // (201 with a GameResponse, 400 with validation errors), so the signature
    // is most of the documentation; see MapGameEndpoints for the rest.
    private static async Task<Results<Created<GameResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateGameRequest request,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        IOpponent computer,
        CancellationToken cancellationToken)
    {
        if (StrengthProblem(request.Opponent) is { } message)
        {
            return FieldProblem(OpponentStrengthField, message);
        }

        var playerId = PlayerId(principal, users);

        // Validation has refused a missing side and a missing kind, so both have values.
        var playerSide = request.PlayerSide!.Value;
        var opponent = ChosenOpponent(request.Opponent);
        var board = rules.NewGame();
        var startingFen = board.Fen;

        // The computer's first move, when it has White, is asked for before
        // anything is saved, so a game is created with it or not at all.
        IReadOnlyList<PlayedMove>? firstMoves = await computer.PlayComputerTurnAsync(
                opponent, playerSide, board, cancellationToken) switch
        {
            MovePlayed first => [first.Move],
            NotComputersTurn => [],
            OpponentUnavailable => null,
            OpponentFault fault => throw new ComputerGameFaultException(gameId: null, fault.Reason),
        };
        if (firstMoves is null)
        {
            return OpponentUnavailableProblem();
        }

        var game = await store.CreateAsync(
            playerId, playerSide, opponent, startingFen, firstMoves, board.Status, cancellationToken);
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
            : TypedResults.Ok(GameMapping.ToResponse(game, game.RebuildBoard(rules)));
    }

    /// <summary>
    /// Plays one move in one of the signed-in player's games, then the
    /// computer's reply when it has the other side, and answers with the game
    /// after them.
    /// </summary>
    // LEARN: The CancellationToken parameter is bound to the request's
    // HttpContext.RequestAborted, which is cancelled when the client goes
    // away, for instance when the browser tab is closed. A Spring MVC
    // handler is given no such signal: a client that has gone is usually
    // noticed only when the response is written. Here the same token is
    // passed to every call that waits: the database queries, the opponent,
    // and through it the wait for a free engine process and for the
    // engine's answer on its standard output. A cancelled request stops at
    // whichever wait it has reached with an OperationCanceledException, which
    // skips the save; and if the cancellation comes just after the computer
    // has answered, the save refuses to begin (see AppendMovesAsync).
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, ProblemHttpResult>> PlayMoveAsync(
        Guid id,
        PlayMoveRequest request,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        IOpponent computer,
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

        // A move meant for another point in the game, such as one sent again
        // after it was saved, or from a tab showing an older position, is
        // refused before it is played, even where it would be legal.
        // Validation has refused a missing number.
        if (request.HalfMoveNumber!.Value != game.NextHalfMoveNumber)
        {
            return ConflictProblem();
        }

        var board = game.RebuildBoard(rules);
        if (game.Opponent.Kind is OpponentKind.Computer)
        {
            RefuseUnplayableComputerGame(game, board, rules);
        }

        return board.TryPlay(request.Uci) switch
        {
            MovePlayed played => await ReplyAndSaveAsync(store, computer, playerId, game, board, played, cancellationToken),
            MalformedMove => FieldProblem(UciField, MalformedMoveMessage),
            IllegalMove => TypedResults.Problem(IllegalMoveDetail, statusCode: StatusCodes.Status422UnprocessableEntity),
            // RebuildBoard refuses a game saved as ongoing whose board is over, so
            // this does not happen; it gets the answer a finished game gets.
            GameAlreadyOver => FinishedProblem(),
        };
    }

    // The computer's reply is saved with the player's move, so in a computer
    // game it is the player's turn whenever a move arrives, and the game was
    // started from the standard position, the only one the opponent plays
    // from. A game saved otherwise is broken, and the player's move is never
    // played for the computer's side.
    private static void RefuseUnplayableComputerGame(SavedGame game, IChessGame board, IChessRules rules)
    {
        if (board.SideToMove != game.PlayerSide)
        {
            throw new ComputerGameFaultException(game.Id, "it is saved with the computer to move");
        }

        if (game.StartingFen != rules.NewGame().Fen)
        {
            throw new ComputerGameFaultException(game.Id, "it does not start from the standard starting position");
        }
    }

    // Asks the computer for its reply when it has one to make, and saves the
    // player's move with it.
    //
    // LEARN: Two requests can play in one game at once: a double click, or
    // two tabs. JPA offers two ways to keep them from both saving. The
    // pessimistic way locks the row when the game is read
    // (LockModeType.PESSIMISTIC_WRITE, SELECT ... FOR UPDATE) and holds the
    // lock until the save commits. The optimistic way reads without a lock
    // and has the save check that the row has not changed since (@Version,
    // with an OptimisticLockException when it has). Here the request is
    // optimistic: the game is loaded with no transaction and no lock, the
    // computer is asked, and only then does AppendMovesAsync take the row
    // lock, for the few milliseconds of its own transaction, and check that
    // the game still has the number of moves this request found. The
    // half-move number plays the part of @Version. A lock held from the load
    // would keep a database connection and transaction open while the engine
    // thinks, and make every other move in this game wait for the engine; a
    // slow or dead engine would hold it until the engine timed out. (Reading
    // the game would not wait: in PostgreSQL a row lock blocks only writes
    // and other locking reads, not a plain SELECT.) A move posted again after
    // the first was saved is refused before the computer is asked, by its
    // half-move number. Two posts of the same move that overlap both ask the
    // computer; the second to reach the lock finds the first's moves saved,
    // and is refused with 409.
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, ProblemHttpResult>> ReplyAndSaveAsync(
        GameStore store,
        IOpponent computer,
        string playerId,
        SavedGame game,
        IChessGame board,
        MovePlayed played,
        CancellationToken cancellationToken)
    {
        // There is no reply to make when the player's move ended the game,
        // or when nobody plays the other side.
        return await computer.PlayComputerTurnAsync(game.Opponent, game.PlayerSide, board, cancellationToken) switch
        {
            MovePlayed reply => await SaveAsync(store, playerId, game, board, [played.Move, reply.Move], cancellationToken),
            NotComputersTurn => await SaveAsync(store, playerId, game, board, [played.Move], cancellationToken),
            OpponentUnavailable => OpponentUnavailableProblem(),
            OpponentFault fault => throw new ComputerGameFaultException(game.Id, fault.Reason),
        };
    }

    // Saves the moves, which have been played on the board, with the status
    // the board is left in, in one transaction, and answers with the game as
    // saved, built from what this request played rather than read back, so a
    // move another request saves just after this one does not appear in this
    // answer.
    private static async Task<Results<Ok<GameResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        GameStore store,
        string playerId,
        SavedGame game,
        IChessGame board,
        IReadOnlyList<PlayedMove> moves,
        CancellationToken cancellationToken)
    {
        var appended = await store.AppendMovesAsync(
            playerId, game.Id, game.NextHalfMoveNumber, moves, board.Status, cancellationToken);
        return appended switch
        {
            MovesAppended saved => TypedResults.Ok(GameMapping.ToResponse(WithMoves(game, moves, saved), board)),
            GameNotFound => NotFoundProblem(),
            GameFinished => FinishedProblem(),
            HalfMoveConflict => ConflictProblem(),
        };
    }

    // The game as the store has just saved it.
    private static SavedGame WithMoves(SavedGame game, IReadOnlyList<PlayedMove> moves, MovesAppended saved) =>
        game with
        {
            Status = saved.Status,
            Result = saved.Result,
            EndReason = saved.EndReason,
            UpdatedAt = saved.UpdatedAt,
            Moves = [.. game.Moves, .. moves],
        };

    // A strength the computer does not offer, or one given for no computer,
    // is refused before any engine is asked.
    private static string? StrengthProblem(OpponentRequest opponent) => opponent switch
    {
        { Kind: OpponentKind.None, Strength: not null } => StrengthWithoutComputerMessage,
        { Kind: OpponentKind.Computer, Strength: int strength } when !OpponentStrengths.IsOffered(strength) => UnofferedStrengthMessage,
        _ => null,
    };

    // The JSON converter refuses numbers, and reads a comma-separated list of
    // names as the members combined bit by bit (see JsonServices). With
    // OpponentKind's two members, 0 and 1, every value it can produce is a
    // named member, so the unnamed-value check (CS8524, see GameStore.WinFor)
    // is switched off here. A third member would make "computer,<new>" an
    // unnamed value that throws, and would need Enum.IsDefined refused with
    // 400 before this switch.
#pragma warning disable CS8524
    private static Opponent ChosenOpponent(OpponentRequest opponent) => opponent.Kind!.Value switch
    {
        OpponentKind.None => Opponent.None,
        OpponentKind.Computer => Opponent.Computer(opponent.Strength ?? OpponentRequest.DefaultStrength),
    };
#pragma warning restore CS8524

    // The group requires a signed-in player, and the session cookie Identity
    // issues always carries the account id, so a missing one is a bug here
    // rather than something to answer.
    private static string PlayerId(ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
        users.GetUserId(principal)
        ?? throw new InvalidOperationException("The signed-in player's session carries no account id.");

    // A validation problem in the shape AddValidation gives its own: one
    // message under one field.
    private static ValidationProblem FieldProblem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static ProblemHttpResult NotFoundProblem() =>
        TypedResults.Problem(GameNotFoundDetail, statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult FinishedProblem() =>
        TypedResults.Problem(GameFinishedDetail, statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult ConflictProblem() =>
        TypedResults.Problem(MoveConflictDetail, statusCode: StatusCodes.Status409Conflict);

    // 503 rather than another 4xx or 5xx: the request was right and the
    // server is not broken, but a service it depends on gave no answer this
    // time, and the same request may succeed when sent again.
    private static ProblemHttpResult OpponentUnavailableProblem() =>
        TypedResults.Problem(OpponentUnavailableDetail, statusCode: StatusCodes.Status503ServiceUnavailable);
}
