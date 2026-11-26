namespace ChessAscent.Chess;

// LEARN: This is how expected failures are reported here. Java would declare
// a checked exception (`throws IllegalMoveException`) and make every caller
// catch it or declare it in turn. C# has no checked exceptions: any method can
// throw anything, and nothing forces a caller to deal with it. So a failure
// that is a normal outcome of user input, such as an illegal move, comes back
// as a value instead, and a switch expression over the union must handle every
// case (see GameStatus). Exceptions stay for bugs and broken environments.
/// <summary>The outcome of <see cref="IChessGame.TryPlay"/>.</summary>
public union PlayResult(MovePlayed, IllegalMove, MalformedMove, GameAlreadyOver)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>Why a move was not played. The failure cases of <see cref="PlayResult"/>.</summary>
public union MoveRejection(IllegalMove, MalformedMove, GameAlreadyOver)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The move was played.</summary>
/// <param name="Move">The move, in both notations.</param>
/// <param name="Status">The game's status after the move.</param>
public readonly record struct MovePlayed(PlayedMove Move, GameStatus Status);

/// <summary>
/// The text is a well-formed UCI move, but not a legal one in this position.
/// That includes a promotion letter on a move that is not a promotion, and a
/// promotion without one. The game is unchanged.
/// </summary>
/// <param name="Uci">The move as given.</param>
public readonly record struct IllegalMove(string Uci);

/// <summary>
/// The text is not a UCI move. A UCI move is two different squares from
/// <c>a1</c> to <c>h8</c> in lower case, such as <c>e2e4</c>, optionally
/// followed by a promotion letter <c>q</c>, <c>r</c>, <c>b</c> or <c>n</c>, as
/// in <c>e7e8q</c>, with nothing before or after. The game is unchanged.
/// </summary>
/// <param name="Input">The text as given.</param>
public readonly record struct MalformedMove(string Input);

/// <summary>The game had already ended, so no move can be played. The game is unchanged.</summary>
/// <param name="Status">How the game ended.</param>
public readonly record struct GameAlreadyOver(GameStatus Status);
