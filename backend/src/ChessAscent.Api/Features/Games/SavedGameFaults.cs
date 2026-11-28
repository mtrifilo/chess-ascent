using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>Rebuilds a saved game's board, refusing a saved game that is broken.</summary>
internal static class SavedGameBoards
{
    /// <summary>
    /// The board for <paramref name="game"/>: its starting position with
    /// every saved move played, through the same checks as live play.
    /// </summary>
    /// <exception cref="UnrebuildableGameException">
    /// The saved game is broken: its starting position is not a FEN the chess
    /// interface accepts, a saved move is refused, the half-move numbers have
    /// a gap, or the game is saved as ongoing but its moves end it. Every later move in such a game would be refused, and reloading
    /// would not change that. A game saved as finished on a board that is not
    /// over is not broken: a resignation ends a game the board knows nothing
    /// about.
    /// </exception>
    // The stored text is left out of the reason, since nothing bounds what a
    // broken row holds.
    public static IChessGame RebuildBoard(this SavedGame game, IChessRules rules)
    {
        if (!game.MovesNumberedInOrder)
        {
            throw new UnrebuildableGameException(game.Id, "its half-move numbers have a gap");
        }

        var board = rules.Load(game.StartingFen, [.. game.Moves.Select(move => move.Uci)]) switch
        {
            GameLoaded loaded => loaded.Game,
            InvalidStartingPosition => throw new UnrebuildableGameException(
                game.Id, "the starting position is not a FEN the chess interface accepts"),
            InvalidMoveInHistory invalid => throw new UnrebuildableGameException(
                game.Id, $"half-move {invalid.Index + 1} is {Describe(invalid.Rejection)}"),
        };

        if (game.Status == GameProgress.Ongoing && board.Status is not Ongoing)
        {
            throw new UnrebuildableGameException(game.Id, "it is saved as ongoing, but its moves end it");
        }

        return board;
    }

    private static string Describe(MoveRejection rejection) => rejection switch
    {
        IllegalMove => "illegal",
        MalformedMove => "not a UCI move",
        GameAlreadyOver => "after the end of the game",
    };
}

/// <summary>
/// A saved game that the chess interface cannot rebuild. What is stored is
/// broken, which is a fault on the server, not something the player did.
/// </summary>
/// <remarks>
/// It is an exception rather than a result value because no caller can do
/// anything about it except report it: <see cref="SavedGameBoards.RebuildBoard"/>
/// throws it, and <see cref="SavedGameFaultFilter"/> turns it into a 500 for
/// every endpoint of the games group.
/// </remarks>
internal sealed class UnrebuildableGameException(Guid gameId, string reason)
    : Exception($"Saved game {gameId} cannot be rebuilt: {reason}.")
{
    public Guid GameId { get; } = gameId;

    /// <summary>What the chess interface refused, without any of the stored text.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// A computer game that cannot be played on, because what is saved, or what
/// the opponent answered about it, is wrong. It is a fault on the server, not
/// something the player did.
/// </summary>
/// <remarks>
/// <see cref="SavedGameFaultFilter"/> turns it into a 500, as it does
/// <see cref="UnrebuildableGameException"/>.
/// </remarks>
/// <param name="gameId">The game, or <c>null</c> for one that was being created and was not saved.</param>
/// <param name="reason">What is wrong, without any text the game or the opponent holds.</param>
internal sealed class ComputerGameFaultException(Guid? gameId, string reason)
    : Exception(gameId is { } id
        ? $"Computer game {id} cannot be played: {reason}."
        : $"A computer game cannot be started: {reason}.")
{
    public Guid? GameId { get; } = gameId;

    public string Reason { get; } = reason;
}

// LEARN: An endpoint filter wraps an endpoint's handler: InvokeAsync runs
// before it, calls `next` to run it (and any filters added after this one),
// and can then inspect or replace what it returned, or catch what it threw.
// It is the minimal API counterpart of an @Around aspect on the handler
// methods, or here of an MVC @ExceptionHandler in a @ControllerAdvice
// limited to some controllers; a HandlerInterceptor, which only runs before
// and after the handler, cannot replace what it returns. Added to a route
// group (see GameEndpoints), it applies to every endpoint in the group and to
// no other. Unlike middleware it runs after routing and parameter binding,
// so it sees the matched endpoint and its bound arguments in the invocation
// context.
/// <summary>
/// Answers 500 when a handler finds a saved game it cannot rebuild, or a
/// computer game it cannot play, after logging which game it was. The game
/// is left as it is.
/// </summary>
internal sealed partial class SavedGameFaultFilter(ILogger<SavedGameFaultFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (UnrebuildableGameException exception)
        {
            LogUnrebuildable(logger, exception.GameId, exception.Reason);
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (ComputerGameFaultException exception)
        {
            if (exception.GameId is { } gameId)
            {
                LogUnplayable(logger, gameId, exception.Reason);
            }
            else
            {
                LogUnstartable(logger, exception.Reason);
            }

            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saved game {GameId} cannot be rebuilt: {Reason}.")]
    private static partial void LogUnrebuildable(ILogger logger, Guid gameId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Computer game {GameId} cannot be played: {Reason}.")]
    private static partial void LogUnplayable(ILogger logger, Guid gameId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "A computer game cannot be started: {Reason}.")]
    private static partial void LogUnstartable(ILogger logger, string reason);
}
