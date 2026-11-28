using ChessAscent.Api.Hints;
using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>Finds the hint for a game's position, checked by the chess rules before it is used.</summary>
internal static class GameHints
{
    /// <summary>Asks the hint provider for the best move on <paramref name="board"/>.</summary>
    /// <param name="hints">The source of hints.</param>
    /// <param name="board">
    /// The game, rebuilt by the chess rules from the standard starting
    /// position, and not over. It is not changed.
    /// </param>
    /// <param name="cancellationToken">Ends the wait for the hint; the call then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The hinted move, as the chess rules list it; <see cref="HintUnavailable"/>
    /// when the provider found no move this time; or <see cref="HintFault"/>
    /// when what it answered means something on the server is wrong.
    /// </returns>
    // Each case of HintReply is mapped by hand, here and nowhere else, so the
    // union never reaches a response, and neither does the reason text of
    // HintFailed, which is written for the log and not for the player.
    public static async Task<HintLookup> FindHintAsync(
        this IHintProvider hints, IChessGame board, CancellationToken cancellationToken)
    {
        var moves = board.History.Select(move => move.Uci).ToList();
        return await hints.FindBestMoveAsync(moves, cancellationToken) switch
        {
            HintMove move => LegalMoveOrFault(board, move),
            HintFailed => new HintUnavailable(),

            // The game was rebuilt by the same rules and is not over, so
            // each of these means the hint provider is broken.
            InvalidMoveInHistory invalid => new HintFault($"the hint engine refused half-move {invalid.Index + 1} of the game"),
            GameAlreadyOver => new HintFault("the hint engine found the game already over"),
        };
    }

    // The provider has already checked its move against the same rules. This
    // check is the one the answer depends on, so a move it refuses means the
    // hint engine is broken, as with the opponent's moves (see ComputerMoves).
    private static HintLookup LegalMoveOrFault(IChessGame board, HintMove move)
    {
        foreach (var legal in board.LegalMoves())
        {
            if (legal.Uci == move.Uci)
            {
                return legal;
            }
        }

        return new HintFault("the hint engine chose a move that is not legal in the position");
    }
}

/// <summary>The outcome of <see cref="GameHints.FindHintAsync"/>.</summary>
internal union HintLookup(LegalMove, HintUnavailable, HintFault)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>
/// The hint provider gave no move this time, for one of the reasons
/// <see cref="HintFailed"/> lists. Asking again may succeed.
/// </summary>
internal readonly record struct HintUnavailable;

/// <summary>What the hint provider answered means the server is at fault: the engine is broken, or the game it was given is wrong.</summary>
/// <param name="Reason">What went wrong, for the log. It holds nothing the engine wrote.</param>
internal readonly record struct HintFault(string Reason);
