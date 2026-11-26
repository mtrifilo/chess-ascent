namespace ChessAscent.Chess;

// LEARN: Interfaces here do the same job as in Java: callers depend on
// IChessRules and IChessGame, never on the class behind them, so the rules
// library can be swapped for a hand-written engine without touching them.
// What enforces the hiding in .NET is `internal` (see ChessRealmsGame): the
// implementation types are invisible outside this assembly, and no public
// signature mentions a type from the library. The I prefix is the .NET naming
// convention for interfaces.
/// <summary>Starts and rebuilds chess games under the standard rules.</summary>
/// <remarks>
/// <para>
/// Implementations hold no state, so one instance may be shared between
/// threads and registered as a singleton. The games they return may not be
/// shared: see <see cref="IChessGame"/>.
/// </para>
/// <para>
/// No method throws for bad input. Each failure is a case of the returned
/// union.
/// </para>
/// </remarks>
public interface IChessRules
{
    /// <summary>Starts a game from the standard starting position.</summary>
    IChessGame NewGame();

    /// <summary>
    /// Rebuilds a game from what is saved: the position it started from and
    /// the moves played since, in order.
    /// </summary>
    /// <param name="initialFen">A six-field FEN, such as the standard starting position's.</param>
    /// <param name="uciMoves">The moves in UCI coordinate form, such as <c>e2e4</c>.</param>
    /// <returns>
    /// The rebuilt game, or the first problem found. A move list that runs
    /// past the end of the game is refused at the first move after the end.
    /// </returns>
    LoadResult Load(string initialFen, IReadOnlyList<string> uciMoves);
}
