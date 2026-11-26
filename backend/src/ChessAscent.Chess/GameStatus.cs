namespace ChessAscent.Chess;

// LEARN: A union type (new in C# 15) says "a value of exactly one of these
// types". It is the counterpart of a Java sealed interface whose permitted
// subclasses are listed in `permits`, with two differences. The cases are
// existing, independent types (here four record structs), not subclasses, so
// the same type can be a case of several unions. And the union itself is a
// small struct that wraps the case value; an assignment such as
// `GameStatus status = new Stalemate();` converts implicitly.
//
// The payoff is close to Java 21's: a switch *expression*
// (`status switch { Ongoing => ..., ... }`) over a union that misses a case
// gets compiler warning CS8509, which this solution's warnings-as-errors turns
// into a build failure. So when a case is added, every switch expression that
// does not handle it stops compiling. Unlike a Java pattern switch statement,
// a C# switch *statement* is not checked, and neither language checks a chain
// of type tests (`is` here, `instanceof` in Java), so code that must handle
// every case uses the expression form.
//
// One trap Java does not have: like every struct, a union has a `default`
// value, and that value holds no case at all. A switch over it matches no
// arm and throws SwitchExpressionException at run time. Nothing in
// ChessAscent.Chess returns `default(GameStatus)`; do not create one.
//
// The unions in this project are domain types, not JSON shapes:
// System.Text.Json writes a case's properties with no tag naming the case, so
// Ongoing and Stalemate both come out as `{}`. An API maps them to its own
// response types.
/// <summary>Where a game stands: still being played, or over and why.</summary>
public union GameStatus(Ongoing, Checkmate, Stalemate, Draw)
{
    // A union's own ToString names only the union type; the case is what a
    // log line or a test failure needs.
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The game is still being played.</summary>
public readonly record struct Ongoing;

/// <summary>The side to move is checkmated.</summary>
/// <param name="Winner">The side that gave mate.</param>
public readonly record struct Checkmate(Side Winner);

/// <summary>The side to move has no legal move and is not in check. The game is drawn.</summary>
public readonly record struct Stalemate;

/// <summary>The game is drawn by rule, without stalemate.</summary>
/// <param name="Reason">The rule that ended it.</param>
public readonly record struct Draw(DrawReason Reason);

/// <summary>The rules that draw a game automatically.</summary>
public enum DrawReason
{
    /// <summary>The same position has occurred three times with the same side to move.</summary>
    ThreefoldRepetition,

    /// <summary>Fifty moves by each side (100 half-moves) without a capture or a pawn move.</summary>
    FiftyMoveRule,

    /// <summary>
    /// Neither side has the material to checkmate: king against king, king
    /// and one bishop or knight against king, or kings with bishops that all
    /// stand on squares of one colour. Other positions where mate is
    /// impossible, such as kings walled off by locked pawns, are not detected
    /// and play on until repetition or the fifty-move rule ends them.
    /// </summary>
    DeadPosition,
}
