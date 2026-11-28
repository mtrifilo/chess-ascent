using ChessAscent.Chess;

namespace ChessAscent.Api.Engine;

/// <summary>
/// Asks an engine for its move in a game that the chess rules have rebuilt,
/// and accepts the answer only when the rules find it legal there.
/// </summary>
/// <remarks>
/// Engines do not refuse bad input or say when they have misread it (see
/// <see cref="UciEngine"/>), so whatever an engine answers is checked. A
/// reply that is not a legal move in the position is an engine fault: it is
/// logged, it is not returned, and the engine is reported broken so that the
/// pool replaces it.
/// </remarks>
internal static partial class LegalMoveSearch
{
    /// <summary>The standard starting position, which <see cref="SearchAsync"/> sends as <c>startpos</c>.</summary>
    public const string StandardStartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // A real move is at most five characters; a longer reply is cut short in
    // the log.
    private const int LoggedReplyLength = 16;

    /// <summary>
    /// Sends the game's moves, as the chess rules recorded them, from the
    /// standard starting position; searches to the limit; and returns the
    /// engine's move if it is legal in the game's position.
    /// </summary>
    /// <param name="lease">The engine, with any options the search depends on already set.</param>
    /// <param name="game">An ongoing game, loaded from <see cref="StandardStartingFen"/>.</param>
    /// <param name="limit">When the search ends.</param>
    /// <param name="engineName">The engine's name, for the log.</param>
    /// <param name="logger">Where an engine fault is logged.</param>
    /// <param name="cancellationToken">Ends the search.</param>
    /// <returns>The move, in UCI coordinate form; or null after an engine fault.</returns>
    public static async Task<string?> SearchAsync(
        EngineLease lease, IChessGame game, SearchLimit limit, string engineName, ILogger logger, CancellationToken cancellationToken)
    {
        SearchResult answer;
        try
        {
            answer = await lease.Engine.SearchAsync(UciPosition.StartingPosition(game.History.Select(move => move.Uci)), limit, cancellationToken);
        }
        catch (UciProtocolException)
        {
            return Fault(lease, engineName, logger, "(bestmove with no move)");
        }

        // LEARN: A `when` clause on a switch arm is a case guard, like a
        // Java 21 `case BestMove b when ...`: the arm matches only if the type
        // pattern matches and the condition holds; otherwise the next arm is
        // tried. The compiler still checks that the arms cover every case of
        // the union, counting the unguarded BestMove arm.
        return answer switch
        {
            BestMove { Move: var move } when IsLegal(game, move) => move,
            BestMove { Move: var move } => Fault(lease, engineName, logger, UciText.ForLog(move.Length > LoggedReplyLength ? move[..LoggedReplyLength] + "..." : move)),
            NoLegalMove => Fault(lease, engineName, logger, "(no move)"),
        };
    }

    private static bool IsLegal(IChessGame game, string move) =>
        game.LegalMoves().Any(legal => legal.Uci == move);

    private static string? Fault(EngineLease lease, string engineName, ILogger logger, string reply)
    {
        LogFault(logger, engineName, lease.Engine.ProcessId, reply);
        lease.ReportBroken();
        return null;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine fault: {Engine} process {ProcessId} replied {Reply}, which is not a legal move in the position it was sent; the reply was not used and the process will be replaced")]
    private static partial void LogFault(ILogger logger, string engine, int processId, string reply);
}
