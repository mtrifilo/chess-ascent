using System.Diagnostics;
using System.Globalization;
using ChessAscent.Api.Engine;
using ChessAscent.Chess;

namespace ChessAscent.Api.Opponents;

/// <summary>
/// The opponent played by Maia-3, a neural network trained to choose the move
/// a human player of a given rating would, run as the engine kind
/// <see cref="EngineKind"/> in the <see cref="EnginePool"/>.
/// </summary>
/// <remarks>
/// <para>
/// Maia keeps its previous position, without a word, when it is sent one it
/// cannot read or an illegal move, and then answers for the wrong position.
/// So nothing it is sent or sends back is trusted: the game is rebuilt with
/// the chess rules before Maia is asked, a finished game is never sent, and
/// a reply that is not a legal move in the position is an engine fault. A
/// fault is logged, the move is not returned, and the process is reported
/// broken so that the pool replaces it.
/// </para>
/// <para>
/// A process is shared by every game, and keeps whatever the previous
/// request set. So each request sets both ratings, the sampling temperature
/// and the number of candidate lines before it asks, and sends the whole
/// game from the starting position, which is also what Maia's move history
/// is rebuilt from.
/// </para>
/// </remarks>
public sealed partial class MaiaOpponent(EnginePool pool, IChessRules rules, ILogger<MaiaOpponent> logger) : IOpponent
{
    /// <summary>The engine kind, in the pool's configuration, that runs Maia.</summary>
    public const string EngineKind = "maia";

    private const string StandardStartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Sampling at 1.0 draws from the predicted distribution of human moves.
    // Taking the most likely move instead (0) plays far above the rating.
    private const string SamplingTemperature = "1.0";

    // Maia prints one line per candidate and scores each of them again, which
    // roughly doubles the cost of a move; only the move itself is used.
    private const string CandidateLines = "1";

    // Maia does not search. It scores the position once, whatever the limit
    // says, so the smallest limit states that.
    private static readonly SearchLimit OneEvaluation = SearchLimit.Nodes(1);

    // A real move is at most five characters; a longer reply is cut short in
    // the log.
    private const int LoggedReplyLength = 16;

    public async Task<OpponentReply> ChooseMoveAsync(IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (!OpponentStrengths.IsOffered(strength))
        {
            return new StrengthNotOffered(strength);
        }

        var loaded = rules.Load(StandardStartingFen, moves);
        if (loaded is InvalidMoveInHistory invalid)
        {
            return invalid;
        }

        var game = loaded is GameLoaded { Game: var rebuilt }
            ? rebuilt
            : throw new UnreachableException("The standard starting position was refused.");
        if (game.Status is not Ongoing)
        {
            return new GameAlreadyOver(game.Status);
        }

        try
        {
            return await pool.RunAsync(EngineKind, lease => AskAsync(lease, game, moves, strength, cancellationToken), cancellationToken);
        }
        // EngineUnavailableException: no process could be had (Maia is not
        // installed, say, or every process is busy). UciEngineException: the
        // process timed out or ended. InvalidOperationException, which
        // includes ObjectDisposedException: the process had become unusable,
        // or the pool is stopping. Cancellation by the caller is not caught.
        catch (Exception e) when (e is EngineUnavailableException or UciEngineException or InvalidOperationException)
        {
            LogUnavailable(logger, e);
            return new OpponentFailed($"The opponent did not answer: {e.Message}");
        }
    }

    private async Task<OpponentReply> AskAsync(
        EngineLease lease, IChessGame game, IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken)
    {
        var engine = lease.Engine;
        var rating = strength.ToString(CultureInfo.InvariantCulture);

        // SelfElo is the rating of the side to move, Maia's own; OppoElo is
        // its opponent's. Both are the chosen strength: Maia then plays as
        // that rating against someone of the same rating.
        await engine.SetOptionAsync("SelfElo", rating, cancellationToken);
        await engine.SetOptionAsync("OppoElo", rating, cancellationToken);
        await engine.SetOptionAsync("Temperature", SamplingTemperature, cancellationToken);
        await engine.SetOptionAsync("MultiPV", CandidateLines, cancellationToken);

        SearchResult answer;
        try
        {
            answer = await engine.SearchAsync(UciPosition.StartingPosition(moves), OneEvaluation, cancellationToken);
        }
        catch (UciProtocolException)
        {
            return Fault(lease, "(no move)");
        }

        // LEARN: A `when` clause on a switch arm is a case guard, like a
        // Java 21 `case BestMove b when ...`: the arm matches only if the type
        // pattern matches and the condition holds; otherwise the next arm is
        // tried. The compiler still checks that the arms cover every case of
        // the union, counting the unguarded BestMove arm.
        return answer switch
        {
            BestMove { Move: var move } when IsLegal(game, move) => new OpponentMove(move),
            BestMove { Move: var move } => Fault(lease, move),
            NoLegalMove => Fault(lease, "(no move)"),
        };
    }

    private static bool IsLegal(IChessGame game, string move) =>
        game.LegalMoves().Any(legal => legal.Uci == move);

    private OpponentFailed Fault(EngineLease lease, string reply)
    {
        var shown = UciText.ForLog(reply.Length > LoggedReplyLength ? reply[..LoggedReplyLength] + "..." : reply);
        LogFault(logger, lease.Engine.ProcessId, shown);
        lease.ReportBroken();
        return new OpponentFailed("The opponent answered with something that is not a legal move in the position.");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine fault: Maia process {ProcessId} replied {Reply}, which is not a legal move in the position it was sent; the reply was not used and the process will be replaced")]
    private static partial void LogFault(ILogger logger, int processId, string reply);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The opponent could not be asked for a move")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
