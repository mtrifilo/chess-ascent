using System.Diagnostics;
using ChessAscent.Api.Engine;
using ChessAscent.Chess;

namespace ChessAscent.Api.Hints;

/// <summary>
/// Hints from Fairy-Stockfish, a classical chess engine (Stockfish with
/// support for chess variants), run as the engine kind
/// <see cref="EngineKind"/> in the <see cref="EnginePool"/> at full strength.
/// </summary>
/// <remarks>
/// <para>
/// Fairy-Stockfish stops reading a move list at an illegal move without a
/// word, and can then answer for the wrong side; given some malformed
/// positions it crashes. So nothing it is sent or sends back is trusted: the
/// game is rebuilt with the chess rules before it is asked, a finished game
/// is never sent, only the moves the rules recorded are sent, and a reply
/// that is not a legal move in the position is an engine fault (see
/// <see cref="LegalMoveSearch"/>).
/// </para>
/// <para>
/// A process is shared by every request, and keeps whatever the previous
/// request set: options, and what its last search left in its hash table.
/// So each request sets full strength and the number of candidate lines,
/// and clears the hash table, before it asks. At full strength and a fixed
/// depth the engine then gives the same move every time for the same game.
/// </para>
/// </remarks>
public sealed partial class FairyStockfishHintProvider(EnginePool pool, IChessRules rules, ILogger<FairyStockfishHintProvider> logger) : IHintProvider
{
    /// <summary>The engine kind, in the pool's configuration, that runs Fairy-Stockfish.</summary>
    /// <remarks>
    /// An underscore, not a hyphen: the kind is part of the names of
    /// environment variables such as <c>EnginePool__Kinds__fairy_stockfish__ExecutablePath</c>,
    /// and a shell cannot set a variable whose name has a hyphen.
    /// </remarks>
    public const string EngineKind = "fairy_stockfish";

    // Full strength: Skill Level at its maximum, 20, turns the engine's
    // deliberate mistakes off, and with UCI_LimitStrength off UCI_Elo is
    // ignored. A process another caller set to play weaker keeps that setting
    // until it is set back.
    private const string FullSkillLevel = "20";
    private const string NoStrengthLimit = "false";

    // Three candidate lines, though only the best is used: the engine
    // searches differently with each number, and three is the number its
    // hints were measured with. At depth 12 its best move then matched a far
    // deeper search by Stockfish on every one of 12 tactical test positions.
    private const string CandidateLines = "3";

    // A fixed depth, never a time: a time limit is used up in full whatever
    // the position, and makes the move depend on the machine's speed.
    private const int SearchDepth = 12;
    private static readonly SearchLimit HintSearch = SearchLimit.Depth(SearchDepth);

    public async Task<HintReply> FindBestMoveAsync(IReadOnlyList<string> moves, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moves);

        var loaded = rules.Load(LegalMoveSearch.StandardStartingFen, moves);
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

        // Configuration that leaves the kind out, as the API tests do, is a
        // failure to report, like any other reason the engine cannot answer.
        if (!pool.IsConfigured(EngineKind))
        {
            return new HintFailed($"No engine kind named \"{EngineKind}\" is configured.");
        }

        try
        {
            return await pool.RunAsync(EngineKind, lease => AskAsync(lease, game, cancellationToken), cancellationToken);
        }
        // EngineUnavailableException: no process could be had (the engine is
        // not installed, say, or every process is busy). UciEngineException:
        // the process timed out or ended. InvalidOperationException, which
        // includes ObjectDisposedException: the process had become unusable,
        // or the pool is stopping. Cancellation by the caller is not caught.
        catch (Exception e) when (e is EngineUnavailableException or UciEngineException or InvalidOperationException)
        {
            LogUnavailable(logger, e);
            return new HintFailed($"The hint engine did not answer: {e.Message}");
        }
    }

    private async Task<HintReply> AskAsync(EngineLease lease, IChessGame game, CancellationToken cancellationToken)
    {
        var engine = lease.Engine;
        await engine.SetOptionAsync("UCI_LimitStrength", NoStrengthLimit, cancellationToken);
        await engine.SetOptionAsync("Skill Level", FullSkillLevel, cancellationToken);
        await engine.SetOptionAsync("MultiPV", CandidateLines, cancellationToken);

        // A button, so it has no value. It empties the hash table and the
        // search's move-ordering history, as ucinewgame would, so that the
        // previous request's search cannot change this one's move.
        await engine.SetOptionAsync("Clear Hash", value: null, cancellationToken);

        return await LegalMoveSearch.SearchAsync(lease, game, HintSearch, "Fairy-Stockfish", logger, cancellationToken) is { } move
            ? new HintMove(move)
            : new HintFailed("The hint engine answered with something that is not a legal move in the position.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The hint engine could not be asked for a move")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
