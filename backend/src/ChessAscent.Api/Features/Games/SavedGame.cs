using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>A game as it is saved, with its moves in the order they were played.</summary>
/// <param name="Id">The game's id.</param>
/// <param name="PlayerSide">The side the player has.</param>
/// <param name="Opponent">Who plays the other side.</param>
/// <param name="StartingFen">
/// The position the game started from. With the UCI form of
/// <paramref name="Moves"/>, this is what <see cref="IChessRules.Load"/>
/// rebuilds the current position from.
/// </param>
/// <param name="Status">Whether the game is still being played.</param>
/// <param name="Result">How the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="EndReason">Why the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="CreatedAt">When the game was created.</param>
/// <param name="UpdatedAt">When the game was created, last had a move saved, or was resigned.</param>
/// <param name="HintCount">The number of positions the player was given a hint in.</param>
/// <param name="Moves">
/// The moves, oldest first. When <paramref name="MovesNumberedInOrder"/>,
/// the move at index <c>i</c> has half-move number <c>i + 1</c>.
/// </param>
/// <param name="MovesNumberedInOrder">
/// Whether the saved half-move numbers run 1, 2, 3 and so on with no gap, as
/// this store writes them. False only when something else wrote to the Moves
/// table; the moves are still in number order, but
/// <see cref="NextHalfMoveNumber"/> is then not the number the next move can
/// be saved at.
/// </param>
// LEARN: A record's generated Equals compares each component with its own
// Equals, which for a list is reference equality: two loads of the same game
// hold two different lists, so they are never == each other. A Java record
// would call List.equals and compare the moves. Compare saved games by their
// members (or the moves with SequenceEqual) instead.
public sealed record SavedGame(
    Guid Id,
    Side PlayerSide,
    Opponent Opponent,
    string StartingFen,
    GameProgress Status,
    GameResult? Result,
    GameEndReason? EndReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int HintCount,
    IReadOnlyList<PlayedMove> Moves,
    bool MovesNumberedInOrder)
{
    /// <summary>
    /// The half-move number the next move will have, which is what
    /// <see cref="GameStore.AppendMovesAsync"/> expects as its first half-move
    /// number.
    /// </summary>
    public int NextHalfMoveNumber => Moves.Count + 1;
}

/// <summary>The outcome of <see cref="GameStore.AppendMovesAsync"/>.</summary>
public union AppendResult(MovesAppended, GameNotFound, GameFinished, HalfMoveConflict)
{
    // A union's own ToString names only the union type; the case is what a
    // log line or a test failure needs.
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The moves were saved, and the game's status and update time with them.</summary>
/// <param name="UpdatedAt">The game's new update time.</param>
/// <param name="Status">The game's progress as saved.</param>
/// <param name="Result">The game's result as saved; <c>null</c> while it is ongoing.</param>
/// <param name="EndReason">Why the game ended, as saved; <c>null</c> while it is ongoing.</param>
/// <param name="HintCount">
/// The game's hint count when the moves were saved, which a hint counted
/// since the caller loaded the game has raised.
/// </param>
public readonly record struct MovesAppended(
    DateTimeOffset UpdatedAt, GameProgress Status, GameResult? Result, GameEndReason? EndReason, int HintCount);

/// <summary>The outcome of <see cref="GameStore.CountHintAsync"/>.</summary>
public union HintCountResult(HintCounted, GameNotFound, GameFinished, HalfMoveConflict)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>
/// The game is at the position the hint is for, and that position is counted
/// in its hint count, by this call or by an earlier one.
/// </summary>
public readonly record struct HintCounted;

/// <summary>The outcome of <see cref="GameStore.ResignAsync"/>.</summary>
public union ResignResult(GameResigned, GameNotFound, GameFinished)
{
    // As for AppendResult: the case is what a log line or a test failure needs.
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The game was ended by the player's resignation.</summary>
/// <param name="Game">The game as the resignation left it.</param>
public readonly record struct GameResigned(SavedGame Game);

/// <summary>
/// No game with that id belongs to that player: either there is none, or it
/// is someone else's. The two are deliberately the same answer. Nothing was
/// saved.
/// </summary>
public readonly record struct GameNotFound;

/// <summary>
/// The game is over, so no move can be added, no hint is counted, it cannot
/// be resigned, and its result stands. Nothing was saved.
/// </summary>
/// <remarks>
/// Not named GameAlreadyOver, which is the chess interface's own case for a
/// move played on a finished board.
/// </remarks>
public readonly record struct GameFinished;

/// <summary>
/// The new moves do not follow the saved ones, or the hint is not for the
/// position the game is in. Nothing was saved.
/// </summary>
/// <remarks>
/// Usually the game does not have exactly one move fewer than the half-move
/// number given, so it is not the game the caller played the moves on or
/// found the hint for: another request saved a move first, or this one was
/// submitted twice.
/// Loading the game again shows where it stands. The other cause is a saved
/// move list with a gap in its numbers, which this store never writes: an
/// append can then land in the gap, or meet a number that is already taken,
/// and reloading does not clear that.
/// </remarks>
public readonly record struct HalfMoveConflict;
