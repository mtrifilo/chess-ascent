using System.Diagnostics;
using ChessAscent.Chess;

namespace ChessAscent.Api.Engine;

/// <summary>
/// Asks an engine of the pool for its move in a game, sending only what the
/// chess rules have accepted and returning only what they find legal.
/// </summary>
/// <remarks>
/// <para>
/// Engines do not refuse bad input or say when they have misread it (see
/// <see cref="UciEngine"/>). So the game is rebuilt with the chess rules
/// before any engine is asked, a finished game is never sent, only the moves
/// the rules recorded are sent, and whatever the engine answers is checked.
/// A reply that is not a legal move in the position is an engine fault: it
/// is logged, it is not returned, and the engine is reported broken so that
/// the pool replaces it.
/// </para>
/// <para>
/// Every way the engine can fail to answer is a <see cref="EngineFailed"/>
/// value, not an exception; only cancellation by the caller throws.
/// </para>
/// </remarks>
internal static partial class LegalMoveSearch
{
    private const string StandardStartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // A real move is at most five characters; a longer reply is cut short in
    // the log.
    private const int LoggedReplyLength = 16;

    /// <summary>Finds the engine's move for the side to move.</summary>
    /// <param name="pool">The pool to borrow the engine from.</param>
    /// <param name="kind">The engine's kind in the pool.</param>
    /// <param name="engineName">The engine's name, for the log.</param>
    /// <param name="role">What the engine is to the caller, such as "The opponent", which begins each failure's reason.</param>
    /// <param name="rules">The chess rules the game is rebuilt and checked with.</param>
    /// <param name="moves">The moves played so far from the standard starting position, in UCI coordinate form.</param>
    /// <param name="configure">Sets the options the search depends on, on the borrowed engine, before it searches.</param>
    /// <param name="limit">When the search ends.</param>
    /// <param name="logger">Where an engine fault, or the reason an engine could not be had, is logged.</param>
    /// <param name="cancellationToken">Ends the wait for the engine and its search; the call then throws <see cref="OperationCanceledException"/>.</param>
    public static async Task<LegalMoveResult> FindAsync(
        EnginePool pool,
        string kind,
        string engineName,
        string role,
        IChessRules rules,
        IReadOnlyList<string> moves,
        Func<UciEngine, CancellationToken, Task> configure,
        SearchLimit limit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        IChessGame game;
        switch (Rebuild(rules, moves))
        {
            case OngoingGame { Game: var ongoing }:
                game = ongoing;
                break;
            case InvalidMoveInHistory invalid:
                return invalid;
            case GameAlreadyOver over:
                return over;
            default:
                throw new UnreachableException();
        }

        // Configuration that leaves the kind out, as the API tests do, is a
        // failure to report, like any other reason the engine cannot answer.
        if (!pool.IsConfigured(kind))
        {
            return new EngineFailed($"No engine kind named \"{kind}\" is configured.");
        }

        try
        {
            return await pool.RunAsync(
                kind,
                async lease =>
                {
                    await configure(lease.Engine, cancellationToken);
                    return await SearchAsync(lease, game, limit, engineName, role, logger, cancellationToken);
                },
                cancellationToken);
        }
        // EngineUnavailableException: no process could be had (the engine is
        // not installed, say, or every process is busy). UciEngineException:
        // the process timed out or ended. InvalidOperationException, which
        // includes ObjectDisposedException: the process had become unusable,
        // or the pool is stopping. Cancellation by the caller is not caught.
        catch (Exception e) when (e is EngineUnavailableException or UciEngineException or InvalidOperationException)
        {
            LogUnavailable(logger, engineName, e);
            return new EngineFailed($"{role} did not answer: {e.Message}");
        }
    }

    /// <summary>
    /// Rebuilds a game from the standard starting position with the chess
    /// rules, before anything is asked for a move in it.
    /// </summary>
    /// <param name="rules">The chess rules the game is rebuilt with.</param>
    /// <param name="moves">The moves played so far, in UCI coordinate form.</param>
    /// <returns>
    /// The game, when it is still being played; the first move the rules
    /// refuse; or, for a finished game, how it ended.
    /// </returns>
    public static RebuiltGame Rebuild(IChessRules rules, IReadOnlyList<string> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        return rules.Load(StandardStartingFen, moves) switch
        {
            InvalidMoveInHistory invalid => invalid,
            GameLoaded { Game.Status: not Ongoing and var status } => new GameAlreadyOver(status),
            GameLoaded { Game: var game } => new OngoingGame(game),
            InvalidStartingPosition => throw new UnreachableException("The standard starting position was refused."),
        };
    }

    // Sends the game from the standard starting position and searches it;
    // the move only if it is legal in the game's position.
    private static async Task<LegalMoveResult> SearchAsync(
        EngineLease lease, IChessGame game, SearchLimit limit, string engineName, string role, ILogger logger, CancellationToken cancellationToken)
    {
        SearchResult answer;
        try
        {
            answer = await lease.Engine.SearchAsync(UciPosition.StartingPosition(game.History.Select(move => move.Uci)), limit, cancellationToken);
        }
        catch (UciProtocolException)
        {
            return Fault(lease, engineName, role, logger, "(bestmove with no move)");
        }

        // LEARN: A `when` clause on a switch arm is a case guard, like a
        // Java 21 `case BestMove b when ...`: the arm matches only if the type
        // pattern matches and the condition holds; otherwise the next arm is
        // tried. The compiler still checks that the arms cover every case of
        // the union, counting the unguarded BestMove arm.
        return answer switch
        {
            BestMove { Move: var move } when IsLegal(game, move) => new EngineMove(move),
            BestMove { Move: var move } => Fault(lease, engineName, role, logger, UciText.ForLog(move.Length > LoggedReplyLength ? move[..LoggedReplyLength] + "..." : move)),
            NoLegalMove => Fault(lease, engineName, role, logger, "(no move)"),
        };
    }

    private static bool IsLegal(IChessGame game, string move) =>
        game.LegalMoves().Any(legal => legal.Uci == move);

    private static EngineFailed Fault(EngineLease lease, string engineName, string role, ILogger logger, string reply)
    {
        LogFault(logger, engineName, lease.Engine.ProcessId, reply);
        lease.ReportBroken();
        return new EngineFailed($"{role} answered with something that is not a legal move in the position.");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine fault: {Engine} process {ProcessId} replied {Reply}, which is not a legal move in the position it was sent; the reply was not used and the process will be replaced")]
    private static partial void LogFault(ILogger logger, string engine, int processId, string reply);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Engine} could not be asked for a move")]
    private static partial void LogUnavailable(ILogger logger, string engine, Exception exception);
}

/// <summary>
/// The outcome of <see cref="LegalMoveSearch.FindAsync"/>: a legal move, or
/// why there is none. The first two failures are the chess interface's own,
/// and in both no engine was asked.
/// </summary>
internal union LegalMoveResult(EngineMove, InvalidMoveInHistory, GameAlreadyOver, EngineFailed);

/// <summary>The outcome of <see cref="LegalMoveSearch.Rebuild"/>: a game still being played, or why a move cannot be chosen in it.</summary>
internal union RebuiltGame(OngoingGame, InvalidMoveInHistory, GameAlreadyOver);

/// <summary>A game the chess rules have rebuilt, which is still being played.</summary>
/// <param name="Game">The game, after every move was played.</param>
internal readonly record struct OngoingGame(IChessGame Game);

/// <summary>The engine's move, which the chess rules have confirmed is legal in the position.</summary>
/// <param name="Uci">The move in UCI coordinate form, such as <c>e2e4</c>.</param>
internal readonly record struct EngineMove(string Uci);

/// <summary>The engine gave no legal move: it could not be had, did not answer in time, or answered with something that is not a legal move.</summary>
/// <param name="Reason">What went wrong, for logs. It holds nothing the engine wrote.</param>
internal readonly record struct EngineFailed(string Reason);
