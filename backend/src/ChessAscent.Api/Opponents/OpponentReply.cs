using ChessAscent.Chess;

namespace ChessAscent.Api.Opponents;

/// <summary>
/// The outcome of <see cref="IOpponent.ChooseMoveAsync"/>: the opponent's
/// move, or why it gave none.
/// </summary>
/// <remarks>
/// Two cases are the chess interface's own, because they mean the same here:
/// <see cref="InvalidMoveInHistory"/> when the moves given are not a legal
/// game, and <see cref="GameAlreadyOver"/> when the game has ended. In both,
/// as with <see cref="StrengthNotOffered"/>, the opponent was not asked.
/// </remarks>
public union OpponentReply(OpponentMove, StrengthNotOffered, InvalidMoveInHistory, GameAlreadyOver, OpponentFailed)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The opponent's move.</summary>
/// <param name="Uci">
/// The move in UCI coordinate form, such as <c>e2e4</c> or <c>e7e8q</c>,
/// which the chess rules have confirmed is legal in the position.
/// </param>
public readonly record struct OpponentMove(string Uci);

/// <summary>The strength is not one of <see cref="OpponentStrengths.Offered"/>.</summary>
/// <param name="Strength">The strength as given.</param>
public readonly record struct StrengthNotOffered(int Strength);

/// <summary>
/// The opponent gave no legal move: it is not installed or not running, it
/// did not answer in time, or it answered with something that is not a legal
/// move in the position. The last is an engine fault, and is logged.
/// </summary>
/// <param name="Reason">What went wrong, for logs. It holds nothing the engine wrote.</param>
public readonly record struct OpponentFailed(string Reason);
