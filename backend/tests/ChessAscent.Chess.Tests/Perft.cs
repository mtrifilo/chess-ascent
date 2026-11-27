using System.Globalization;
using System.Text;

namespace ChessAscent.Chess.Tests;

/// <summary>
/// Perft: the number of positions reachable from a position in exactly N
/// half-moves, counted by playing every legal move. Comparing the counts with
/// published ones checks move generation, including castling, en passant,
/// promotion and pins, without any expected move list.
/// </summary>
/// <remarks>
/// Everything goes through <see cref="IChessGame"/>, so the same counts check
/// any engine behind the interface. The interface has no undo, so each move is
/// played on a <see cref="IChessGame.Branch"/> of its parent, which is far
/// slower than a library's own make-and-undo perft.
/// </remarks>
internal static class Perft
{
    /// <summary>Counts the positions <paramref name="depth"/> half-moves ahead.</summary>
    public static long Count(IChessGame game, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        if (depth == 0)
        {
            return 1;
        }

        ThrowIfDrawn(game, depth);
        var moves = game.LegalMoves();

        // Bulk counting: one ply from the end, each legal move is one leaf, so
        // the last ply needs no branches. Those moves are listed but never
        // played here; that every listed move can be played as listed is
        // checked by RandomGameTests, on the positions its games reach.
        if (depth == 1)
        {
            return moves.Count;
        }

        long total = 0;
        foreach (var move in moves)
        {
            total += Count(PlayOnBranch(game, move.Uci), depth - 1);
        }

        return total;
    }

    /// <summary>
    /// Perft split by first move: for each legal move, the positions
    /// <paramref name="depth"/> half-moves ahead that start with it, in UCI order.
    /// </summary>
    public static IReadOnlyList<(string Uci, long Count)> Divide(IChessGame game, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        ThrowIfDrawn(game, depth);
        return game.LegalMoves()
            .Select(move => (move.Uci, Count(PlayOnBranch(game, move.Uci), depth - 1)))
            .ToArray();
    }

    /// <summary>
    /// A divide in the layout of Stockfish's <c>go perft</c>: one
    /// <c>move: count</c> line per move, in UCI order, and then the total.
    /// Stockfish lists the moves in its own order, so sort both outputs
    /// before comparing them with <c>diff</c> to find the move whose count differs.
    /// </summary>
    public static string Format(IReadOnlyList<(string Uci, long Count)> divide)
    {
        var text = new StringBuilder();
        foreach (var (uci, count) in divide)
        {
            text.Append(CultureInfo.InvariantCulture, $"{uci}: {count}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"\nNodes searched: {divide.Sum(line => line.Count)}\n");
        return text.ToString();
    }

    private static IChessGame PlayOnBranch(IChessGame game, string uci)
    {
        var branch = game.Branch();
        Games.PlayOn(branch, uci);
        return branch;
    }

    // Published perft counts ignore draws: they count every legal move in every
    // position. The interface ends a game at once on repetition, the fifty-move
    // rule or a dead position and then lists no moves, so a drawn position
    // inside the tree would quietly lower the count. None of the standard
    // positions reaches one at the depths run here; this makes sure of it.
    private static void ThrowIfDrawn(IChessGame game, int depth)
    {
        if (game.Status.Value is Draw draw)
        {
            throw new InvalidOperationException(
                $"Perft reached a position drawn by {draw.Reason} with {depth} half-moves still to count: {game.Fen}. "
                + "Published counts play on through draws, so this count cannot be compared with them.");
        }
    }
}
