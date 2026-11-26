namespace ChessAscent.Chess;

// LEARN: This is how expected failures are reported here. Java would declare
// a checked exception (`throws IllegalMoveException`) and make every caller
// catch it. C# has no checked exceptions: any method can throw anything, and
// nothing forces a caller to deal with it. So a failure that is a normal
// outcome of user input, such as an illegal move, comes back as a value
// instead, and the union makes the caller's `switch` handle every case.
// Exceptions stay for bugs and broken environments.
/// <summary>The outcome of <see cref="IChessGame.TryPlay"/>.</summary>
public union PlayResult(MovePlayed, IllegalMove, MalformedMove, GameAlreadyOver);

/// <summary>Why a move was not played. The failure cases of <see cref="PlayResult"/>.</summary>
public union MoveRejection(IllegalMove, MalformedMove, GameAlreadyOver);

/// <summary>The move was played.</summary>
/// <param name="Move">The move, in both notations.</param>
/// <param name="Status">The game's status after the move.</param>
public readonly record struct MovePlayed(PlayedMove Move, GameStatus Status);

/// <summary>
/// The text is a well-formed UCI move, but not a legal one in this position.
/// The game is unchanged.
/// </summary>
/// <param name="Uci">The move as given.</param>
public readonly record struct IllegalMove(string Uci);

/// <summary>
/// The text is not a UCI move: four or five lowercase characters such as
/// <c>e2e4</c> or <c>e7e8q</c>. The game is unchanged.
/// </summary>
/// <param name="Input">The text as given.</param>
public readonly record struct MalformedMove(string Input);

/// <summary>The game had already ended, so no move can be played. The game is unchanged.</summary>
/// <param name="Status">How the game ended.</param>
public readonly record struct GameAlreadyOver(GameStatus Status);
