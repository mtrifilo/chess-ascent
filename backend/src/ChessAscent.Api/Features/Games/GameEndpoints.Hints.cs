using System.Security.Claims;
using ChessAscent.Api.Hints;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ChessAscent.Api.Features.Games;

/// <remarks>
/// <para>
/// The answers a hint request can get, so the game screen can react to each:
/// 200 with the hint; 400 with a validation problem under
/// <c>HalfMoveNumber</c> when that is missing, and a plain 400 problem when
/// the body cannot be read; 415 when its content type is not JSON; 401; 403
/// when a page on another origin started the request; 404 when the player
/// has no game with that id; 422 with <see cref="HintWithoutComputerDetail"/>
/// when nobody plays the other side, since only games against the computer
/// get hints; 409 with <see cref="HintGameFinishedDetail"/> when the game is
/// over; 409 with <see cref="HintConflictDetail"/> when the half-move number
/// is not the game's next one, or a move was saved while the hint was being
/// found; 500 when the game's saved form is broken, it is saved with the
/// computer to move, or what the hint engine answered means the server is
/// at fault; and 503 with <see cref="HintUnavailableDetail"/> when the hint
/// engine gave no move.
/// </para>
/// <para>
/// The answers follow a move's: both 409s mean the game is not where the
/// screen thought, and the screen loads it again; a 503 can be sent again
/// unchanged. The 422, like a move's, means the request can never succeed as
/// it is: a game without the computer never gets a hint.
/// </para>
/// <para>
/// A hint is never played, and is never stored. The game counts the
/// positions in which it gave one (see <see cref="GameStore.CountHintAsync"/>),
/// so asking again in the same position, after a reload or a double click,
/// gets the same answer and is not counted again. Only a 200 counts.
/// </para>
/// </remarks>
public static partial class GameEndpoints
{
    /// <summary>The detail of the 422 for a hint in a game in which nobody plays the other side.</summary>
    public const string HintWithoutComputerDetail = "Hints are given only in games against the computer.";

    /// <summary>The detail of the 409 for a hint in a game that is over.</summary>
    public const string HintGameFinishedDetail = "The game is over, so there is no hint to give.";

    /// <summary>
    /// The detail of the 409 for a hint whose half-move number is not the
    /// game's next one, or in a game in which a move was saved while the hint
    /// was being found.
    /// </summary>
    public const string HintConflictDetail =
        "No hint was given: the game is not where the request expected it, because a move was saved or the half-move number is not the game's next one. Load the game to see where it stands.";

    /// <summary>
    /// The detail of the 503 for a hint the hint engine did not find. Nothing
    /// was recorded, and the same request can be sent again.
    /// </summary>
    public const string HintUnavailableDetail =
        "No hint could be found this time, so nothing was recorded. Send the same request again to try once more.";

    /// <summary>
    /// The hint engine's best move in one of the signed-in player's games
    /// against the computer, on the player's turn, without playing it.
    /// </summary>
    private static async Task<Results<Ok<HintResponse>, ProblemHttpResult>> GiveHintAsync(
        Guid id,
        HintRequest request,
        ClaimsPrincipal principal,
        UserManager<IdentityUser> users,
        GameStore store,
        IChessRules rules,
        IHintProvider hints,
        CancellationToken cancellationToken)
    {
        var playerId = PlayerId(principal, users);
        var game = await store.LoadAsync(playerId, id, cancellationToken);
        if (game is null)
        {
            return NotFoundProblem();
        }

        if (game.Opponent.Kind is not OpponentKind.Computer)
        {
            return TypedResults.Problem(HintWithoutComputerDetail, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // The saved status decides, as it does for a move.
        if (game.Status == GameProgress.Finished)
        {
            return HintFinishedProblem();
        }

        // Checked before the engine is asked, so a tab showing an older
        // position is never handed squares for the current one. Validation
        // has refused a missing number.
        if (request.HalfMoveNumber!.Value != game.NextHalfMoveNumber)
        {
            return HintConflictProblem();
        }

        // Only a position the chess rules have rebuilt goes to the engine,
        // and only the moves they recorded. A game saved with the computer to
        // move, or from another starting position, is broken, as it is for a
        // move; the hint engine, like the opponent, plays only from the
        // standard starting position.
        var board = game.RebuildBoard(rules);
        RefuseUnplayableComputerGame(game, board, rules);

        var moves = board.History.Select(move => move.Uci).ToList();
        LegalMove? hinted = await hints.FindBestMoveAsync(moves, cancellationToken) switch
        {
            HintMove move => LegalMoveOrFault(game, board, move),
            HintFailed => null,

            // The game was rebuilt by the same rules and is not over, so
            // each of these means the hint provider is broken.
            InvalidMoveInHistory invalid => throw new HintFaultException(
                game.Id, $"the hint engine refused half-move {invalid.Index + 1} of the game"),
            GameAlreadyOver => throw new HintFaultException(game.Id, "the hint engine found the game already over"),
        };
        if (hinted is not { } legal)
        {
            return HintUnavailableProblem();
        }

        return await store.CountHintAsync(playerId, game.Id, game.NextHalfMoveNumber, cancellationToken) switch
        {
            HintCounted => TypedResults.Ok(new HintResponse(legal.From.ToString(), legal.To.ToString())),
            GameNotFound => NotFoundProblem(),
            GameFinished => HintFinishedProblem(),
            HalfMoveConflict => HintConflictProblem(),
        };
    }

    // The provider has already checked its move against the same rules. This
    // check is the one the answer depends on, so a move it refuses means the
    // hint engine is broken, as with the opponent's moves (see ComputerMoves).
    private static LegalMove LegalMoveOrFault(SavedGame game, IChessGame board, HintMove move) =>
        board.LegalMoves().Where(legal => legal.Uci == move.Uci).Cast<LegalMove?>().SingleOrDefault()
        ?? throw new HintFaultException(game.Id, "the hint engine chose a move that is not legal in the position");

    private static ProblemHttpResult HintFinishedProblem() =>
        TypedResults.Problem(HintGameFinishedDetail, statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult HintConflictProblem() =>
        TypedResults.Problem(HintConflictDetail, statusCode: StatusCodes.Status409Conflict);

    // 503 for the reason a move's is: the request was right, but the hint
    // engine gave no answer this time.
    private static ProblemHttpResult HintUnavailableProblem() =>
        TypedResults.Problem(HintUnavailableDetail, statusCode: StatusCodes.Status503ServiceUnavailable);
}
