namespace ChessAscent.Api.Features.Games;

// The member names of these enumerations, and of ChessAscent.Chess.Side, are
// what the Games table stores (see GameConfiguration), and in camel case
// what the API writes (see GameContracts). Adding a member or reordering
// them is safe for the table; renaming one leaves the rows that hold the old
// name unreadable until a migration rewrites them, and changes the API.

/// <summary>Who plays the side the player does not.</summary>
public enum OpponentKind
{
    /// <summary>Nobody: the player moves both sides.</summary>
    None,

    /// <summary>The computer, at the strength in <see cref="Opponent.Strength"/>.</summary>
    Computer,
}

/// <summary>Whether a game is still being played.</summary>
/// <remarks>
/// Not called GameStatus, so it does not clash with
/// <see cref="ChessAscent.Chess.GameStatus"/>, the board's own view of the
/// same question, in code that uses both.
/// </remarks>
public enum GameProgress
{
    Ongoing,
    Finished,
}

/// <summary>How a finished game ended for the two sides.</summary>
public enum GameResult
{
    WhiteWins,
    BlackWins,
    Draw,
}

/// <summary>Why a finished game ended.</summary>
public enum GameEndReason
{
    Checkmate,
    Stalemate,
    ThreefoldRepetition,
    FiftyMoveRule,
    DeadPosition,
    Resignation,
}
