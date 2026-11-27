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
/// <param name="UpdatedAt">When the game was created or last had a move saved.</param>
/// <param name="Moves">
/// The moves, oldest first: the move at index <c>i</c> has half-move number
/// <c>i + 1</c>.
/// </param>
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
    IReadOnlyList<PlayedMove> Moves)
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
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The moves were saved, and the game's status and update time with them.</summary>
/// <param name="UpdatedAt">The game's new update time.</param>
public readonly record struct MovesAppended(DateTimeOffset UpdatedAt);

/// <summary>
/// No game with that id belongs to that player: either there is none, or it
/// is someone else's. The two are deliberately the same answer. Nothing was
/// saved.
/// </summary>
public readonly record struct GameNotFound;

/// <summary>
/// The game is over, so no move can be added and its result stands. Nothing
/// was saved.
/// </summary>
/// <remarks>
/// Not named GameAlreadyOver, which is the chess interface's own case for a
/// move played on a finished board.
/// </remarks>
public readonly record struct GameFinished;

/// <summary>
/// The game does not have exactly one move fewer than the first half-move
/// number given, so it is not the game the caller played the moves on:
/// another request saved a move first, or this one was submitted twice.
/// Nothing was saved. Load the game again to see where it stands.
/// </summary>
public readonly record struct HalfMoveConflict;
