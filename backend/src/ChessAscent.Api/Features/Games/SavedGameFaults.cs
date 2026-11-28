namespace ChessAscent.Api.Features.Games;

/// <summary>
/// A saved game that the chess interface cannot rebuild. What is stored is
/// broken, which is a fault on the server, not something the player did.
/// </summary>
/// <remarks>
/// It is an exception rather than a result value because no caller can do
/// anything about it except report it: <see cref="SavedGameFaultFilter"/>
/// turns it into a 500 for every endpoint of the games group.
/// </remarks>
internal sealed class UnrebuildableGameException(Guid gameId, string reason)
    : Exception($"Saved game {gameId} cannot be rebuilt: {reason}.")
{
    public Guid GameId { get; } = gameId;

    /// <summary>What the chess interface refused, without any of the stored text.</summary>
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
// no other. Unlike middleware
// it runs after routing and parameter binding, so it sees the matched
// endpoint and its bound arguments in the invocation context.
/// <summary>
/// Answers 500 when a handler finds a saved game it cannot rebuild, after
/// logging which game it was. The game is left as it is.
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
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saved game {GameId} cannot be rebuilt: {Reason}.")]
    private static partial void LogUnrebuildable(ILogger logger, Guid gameId, string reason);
}
