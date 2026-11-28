namespace ChessAscent.Api.Hints;

/// <summary>The source of hints: given a game, it finds the best move for the side to move.</summary>
/// <remarks>
/// <para>
/// The interface says nothing about how the move is found. Today a chess
/// engine in a separate program finds it (<see cref="FairyStockfishHintProvider"/>);
/// another engine could take its place behind the same interface.
/// </para>
/// <para>
/// The move it returns is legal in the position, checked by the chess rules.
/// It is the same move each time for the same game.
/// </para>
/// <para>
/// Implementations may be called from several requests at once.
/// </para>
/// </remarks>
public interface IHintProvider
{
    /// <summary>Finds the best move for the side to move.</summary>
    /// <param name="moves">
    /// The moves played so far from the standard starting position, oldest
    /// first, in UCI coordinate form such as <c>e2e4</c> or <c>e7e8q</c>.
    /// </param>
    /// <param name="cancellationToken">Ends the wait for the move; the call then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The move, or why there is none. Each failure is a case of the union,
    /// not an exception; only cancellation and a null list throw.
    /// </returns>
    Task<HintReply> FindBestMoveAsync(IReadOnlyList<string> moves, CancellationToken cancellationToken);
}
