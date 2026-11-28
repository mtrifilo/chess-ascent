using ChessAscent.Chess;

namespace ChessAscent.Api.Hints;

/// <summary>
/// The outcome of <see cref="IHintProvider.FindBestMoveAsync"/>: the best
/// move, or why there is none.
/// </summary>
/// <remarks>
/// Two cases are the chess interface's own, because they mean the same here:
/// <see cref="InvalidMoveInHistory"/> when the moves given are not a legal
/// game, and <see cref="GameAlreadyOver"/> when the game has ended. In both,
/// no engine was asked.
/// </remarks>
public union HintReply(HintMove, InvalidMoveInHistory, GameAlreadyOver, HintFailed)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The best move for the side to move.</summary>
/// <param name="Uci">
/// The move in UCI coordinate form, such as <c>e2e4</c> or <c>e7e8q</c>,
/// which the chess rules have confirmed is legal in the position.
/// </param>
public readonly record struct HintMove(string Uci);

/// <summary>
/// No hint could be found: the engine is not installed or not running, it
/// did not answer in time, or it answered with something that is not a legal
/// move in the position. The last is an engine fault, and is logged.
/// </summary>
/// <param name="Reason">What went wrong, for logs. It holds nothing the engine wrote.</param>
public readonly record struct HintFailed(string Reason);
