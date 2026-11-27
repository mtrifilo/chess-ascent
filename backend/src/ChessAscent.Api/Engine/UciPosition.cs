namespace ChessAscent.Api.Engine;

/// <summary>A position to send to an engine: a starting point and the moves played from it.</summary>
/// <remarks>
/// This type checks only that the text cannot break the protocol's framing:
/// no line breaks or other control characters, and each move a single token.
/// It does not check chess. Whether the FEN is valid and the moves legal is
/// for the caller to establish before building one (see <see cref="UciEngine"/>).
/// </remarks>
public sealed class UciPosition
{
    private UciPosition(string command) => Command = command;

    /// <summary>The <c>position</c> command, as it is written to the engine.</summary>
    public string Command { get; }

    /// <summary>The standard starting position, then <paramref name="moves"/>.</summary>
    /// <param name="moves">Moves in UCI coordinate form, such as <c>e2e4</c>, in the order played.</param>
    /// <exception cref="ArgumentException">A move is empty, or holds white space or a control character.</exception>
    public static UciPosition StartingPosition(IEnumerable<string> moves) =>
        new(WithMoves("position startpos", moves));

    /// <summary>The position a FEN describes, then <paramref name="moves"/>.</summary>
    /// <param name="fen">The position in Forsyth-Edwards Notation.</param>
    /// <param name="moves">Moves in UCI coordinate form, such as <c>e2e4</c>, in the order played.</param>
    /// <exception cref="ArgumentException">
    /// The FEN is blank, holds a control character or the word <c>moves</c>;
    /// or a move is empty, or holds white space or a control character.
    /// </exception>
    public static UciPosition FromFen(string fen, IEnumerable<string> moves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);
        UciText.ThrowIfControlCharacter(fen, nameof(fen));

        // The engine reads every token up to "moves" as the FEN.
        if (UciText.HasToken(fen, "moves"))
        {
            throw new ArgumentException("A FEN cannot contain the word \"moves\".", nameof(fen));
        }

        return new(WithMoves($"position fen {fen.Trim()}", moves));
    }

    public override string ToString() => Command;

    private static string WithMoves(string start, IEnumerable<string> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var list = moves.ToList();
        foreach (var move in list)
        {
            ArgumentException.ThrowIfNullOrEmpty(move, nameof(moves));
            if (move.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new ArgumentException($"A move must be one token with no white space or control characters: \"{move}\".", nameof(moves));
            }
        }

        return list.Count == 0 ? start : $"{start} moves {string.Join(' ', list)}";
    }
}
