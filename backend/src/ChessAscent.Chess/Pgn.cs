namespace ChessAscent.Chess;

// LEARN: `init` accessors make these properties settable only in an object
// initializer (`new PgnTags { White = "Ana" }`) or a `with` expression, and
// read-only afterwards. That replaces both a Java builder and a constructor
// with six optional arguments.
/// <summary>
/// The six descriptive tags of PGN's Seven Tag Roster. The seventh, Result,
/// comes from the declared result or the board. A tag left <c>null</c> is
/// written as <c>?</c>, and a missing date as <c>????.??.??</c>, as the PGN
/// standard prescribes for unknown values.
/// </summary>
public sealed record PgnTags
{
    public string? Event { get; init; }

    public string? Site { get; init; }

    public DateOnly? Date { get; init; }

    public string? Round { get; init; }

    public string? White { get; init; }

    public string? Black { get; init; }
}

/// <summary>
/// A result to record in PGN that the board alone does not show, such as a
/// resignation (the other side wins).
/// </summary>
public enum DeclaredResult
{
    /// <summary>Written as <c>1-0</c>.</summary>
    WhiteWins,

    /// <summary>Written as <c>0-1</c>.</summary>
    BlackWins,

    /// <summary>Written as <c>1/2-1/2</c>.</summary>
    Draw,
}

/// <summary>The outcome of <see cref="IChessGame.ToPgn"/>.</summary>
public union PgnExportResult(PgnExported, DeclaredResultContradictsBoard, InvalidPgnTagValue);

/// <summary>The game as PGN text.</summary>
/// <param name="Pgn">The tags, a blank line, the moves and the result, with LF line endings.</param>
public readonly record struct PgnExported(string Pgn);

/// <summary>
/// The game has ended on the board with a different result from the one declared.
/// </summary>
/// <param name="Declared">The result the caller declared.</param>
/// <param name="Status">How the game ended on the board.</param>
public readonly record struct DeclaredResultContradictsBoard(DeclaredResult Declared, GameStatus Status);

/// <summary>
/// A tag value PGN cannot hold: longer than 255 UTF-16 code units, or
/// containing a control character, a line or paragraph separator, or an
/// unpaired surrogate.
/// </summary>
/// <param name="TagName">
/// The tag, such as <c>White</c>. <c>FEN</c> means the game's starting
/// position, which PGN records as a tag when it is not the standard one.
/// </param>
public readonly record struct InvalidPgnTagValue(string TagName);
