namespace ChessAscent.Chess;

// LEARN: A union type (new in C# 15) says "a value of exactly one of these
// types". It is the counterpart of a Java sealed interface whose permitted
// subclasses are listed in `permits`, with two differences. The cases are
// existing, independent types (here four record structs), not subclasses, so
// the same type can be a case of several unions. And the union itself is a
// small struct that wraps the case value; an assignment such as
// `GameStatus status = new Stalemate();` converts implicitly.
//
// The payoff is the same as in Java 21: a `switch` expression over a union
// that misses a case gets compiler warning CS8509, which this solution's
// warnings-as-errors turns into a build failure. So when a case is added,
// every switch that does not handle it stops compiling.
//
// One trap Java does not have: like every struct, a union has a `default`
// value, and that value holds no case at all. A switch over it matches no
// arm and throws SwitchExpressionException at run time. Nothing in this
// library returns `default(GameStatus)`; do not create one.
/// <summary>Where a game stands: still being played, or over and why.</summary>
public union GameStatus(Ongoing, Checkmate, Stalemate, Draw);

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

    /// <summary>Neither side can checkmate by any sequence of legal moves, such as king against king.</summary>
    DeadPosition,
}
