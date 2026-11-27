namespace ChessAscent.Api.Opponents;

/// <summary>The computer opponent: given a game and a strength, it chooses the computer's next move.</summary>
/// <remarks>
/// <para>
/// The interface says nothing about how the opponent runs. Today it is a
/// neural network in a separate program (<see cref="MaiaOpponent"/>); the
/// same model is expected to run inside this process later, behind the same
/// interface.
/// </para>
/// <para>
/// The move it returns is legal in the position, checked by the chess rules,
/// but it is not chosen deterministically: the same game at the same strength
/// can get a different reply each time, as a human opponent's would.
/// </para>
/// <para>
/// Implementations may be called from several requests at once.
/// </para>
/// </remarks>
public interface IOpponent
{
    /// <summary>Chooses the move the side to move plays next.</summary>
    /// <param name="moves">
    /// The moves played so far from the standard starting position, oldest
    /// first, in UCI coordinate form such as <c>e2e4</c> or <c>e7e8q</c>.
    /// </param>
    /// <param name="strength">The strength to play at: one of <see cref="OpponentStrengths.Offered"/>.</param>
    /// <param name="cancellationToken">Ends the wait for a reply; the call then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The move, or why there is none. Each failure is a case of the union,
    /// not an exception; only cancellation and a null list throw.
    /// </returns>
    Task<OpponentReply> ChooseMoveAsync(IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken);
}
