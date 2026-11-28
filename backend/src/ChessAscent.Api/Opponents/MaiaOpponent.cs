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
public sealed class MaiaOpponent(EnginePool pool, IChessRules rules, ILogger<MaiaOpponent> logger) : IOpponent
{
    /// <summary>The engine kind, in the pool's configuration, that runs Maia.</summary>
    public const string EngineKind = "maia";

    // Sampling at 1.0 draws from the predicted distribution of human moves.
    // Taking the most likely move instead (0) plays far above the rating.
    private const string SamplingTemperature = "1.0";

    // Maia prints one line per candidate and scores each of them again, which
    // roughly doubles the cost of a move; only the move itself is used.
    private const string CandidateLines = "1";

    // Maia does not search. It scores the position once, whatever the limit
    // says, so the smallest limit states that.
    private static readonly SearchLimit OneEvaluation = SearchLimit.Nodes(1);

    /// <summary>The engine's name, in the log and in messages.</summary>
    internal const string EngineName = "Maia";

    public async Task<OpponentReply> ChooseMoveAsync(IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (!OpponentStrengths.IsOffered(strength))
        {
            return new StrengthNotOffered(strength);
        }

        var rating = strength.ToString(CultureInfo.InvariantCulture);
        var result = await LegalMoveSearch.FindAsync(
            pool, EngineKind, EngineName, "The opponent", rules, moves, (engine, token) => ConfigureAsync(engine, rating, token), OneEvaluation, logger, cancellationToken);
        return result switch
        {
            EngineMove { Uci: var move } => new OpponentMove(move),
            InvalidMoveInHistory invalid => invalid,
            GameAlreadyOver over => over,
            EngineFailed { Reason: var reason } => new OpponentFailed(reason),
        };
    }

    private static async Task ConfigureAsync(UciEngine engine, string rating, CancellationToken cancellationToken)
    {
        // SelfElo is the rating of the side to move, Maia's own; OppoElo is
        // its opponent's. Both are the chosen strength: Maia then plays as
        // that rating against someone of the same rating.
        await engine.SetOptionAsync("SelfElo", rating, cancellationToken);
        await engine.SetOptionAsync("OppoElo", rating, cancellationToken);
        await engine.SetOptionAsync("Temperature", SamplingTemperature, cancellationToken);
        await engine.SetOptionAsync("MultiPV", CandidateLines, cancellationToken);
    }
}
