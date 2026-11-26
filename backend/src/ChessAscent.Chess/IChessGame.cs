namespace ChessAscent.Chess;

/// <summary>A game of chess in progress or finished, played under the standard rules.</summary>
/// <remarks>
/// <para>
/// Threading: an instance is mutable and not safe to use from more than one
/// thread, even for reads. Create one per request with
/// <see cref="IChessRules.NewGame"/> or <see cref="IChessRules.Load"/>, use it
/// on that request only, and never cache or share it. Use
/// <see cref="Branch"/> for an independent copy, for example to explore a line
/// without changing the game.
/// </para>
/// <para>
/// Draws: threefold repetition, the fifty-move rule and dead positions (as far
/// as <see cref="DrawReason.DeadPosition"/> describes them) end the game
/// automatically, with no claim needed from either player. A repetition
/// or fifty-move draw takes effect immediately after the move that completes
/// it, so that move is played and recorded and the game is then over.
/// </para>
/// <para>
/// No member throws for bad input: each failure is a case of the returned
/// union. A null argument, which the nullable annotations already rule out,
/// throws <see cref="ArgumentNullException"/>, and an undefined enum value
/// throws <see cref="ArgumentOutOfRangeException"/>. Those are bugs in the
/// caller, not input to report back.
/// </para>
/// </remarks>
public interface IChessGame
{
    /// <summary>The current position as six-field FEN.</summary>
    string Fen { get; }

    /// <summary>
    /// The side whose turn it is. After checkmate, the side that was mated.
    /// </summary>
    Side SideToMove { get; }

    /// <summary>Where the game stands.</summary>
    GameStatus Status { get; }

    /// <summary>
    /// The moves played since the starting position, oldest first. Each read
    /// returns a new list, which later moves do not change.
    /// </summary>
    IReadOnlyList<PlayedMove> History { get; }

    /// <summary>Every legal move for the side to move, ordered by UCI text. Empty once the game is over.</summary>
    IReadOnlyList<LegalMove> LegalMoves();

    /// <summary>
    /// The legal moves of the piece on <paramref name="square"/>, ordered by
    /// UCI text. Empty when the square is empty, holds the opponent's piece,
    /// or the piece cannot move, and once the game is over.
    /// </summary>
    IReadOnlyList<LegalMove> LegalMovesFrom(Square square);

    /// <summary>
    /// Plays a move given in UCI coordinate form, such as <c>e2e4</c>, or
    /// <c>e7e8q</c> for a promotion. A promotion needs its piece letter; there
    /// is no default queen. Anything other than a played move leaves the game
    /// unchanged.
    /// </summary>
    PlayResult TryPlay(string uciMove);

    /// <summary>An independent copy of the game: moves played on either one do not affect the other.</summary>
    IChessGame Branch();

    /// <summary>Exports the game as PGN with the Seven Tag Roster.</summary>
    /// <param name="tags">The descriptive tags.</param>
    /// <param name="declaredResult">
    /// The result to record, such as a win for the other side when a player
    /// resigns. <c>null</c> records the board's result, which is <c>*</c>
    /// while the game is ongoing. A declared result must agree with the
    /// board once the game has ended there.
    /// </param>
    PgnExportResult ToPgn(PgnTags tags, DeclaredResult? declaredResult);
}
