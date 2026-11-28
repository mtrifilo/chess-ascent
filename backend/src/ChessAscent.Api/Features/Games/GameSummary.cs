using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>A game as a list of games shows it: what it is and where it stands, without its moves.</summary>
/// <param name="Id">The game's id.</param>
/// <param name="PlayerSide">The side the player has.</param>
/// <param name="OpponentKind">Who plays the other side.</param>
/// <param name="OpponentStrength">The computer's strength setting; <c>null</c> when there is no opponent.</param>
/// <param name="Status">Whether the game is still being played.</param>
/// <param name="Result">How the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="EndReason">Why the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="HalfMoveCount">The number of half-moves saved.</param>
/// <param name="HintCount">The number of positions the player was given a hint in.</param>
/// <param name="UpdatedAt">When the game was created, last had a move saved, or was resigned.</param>
// The opponent is the two stored columns, not an Opponent, so a row saved
// with a computer and no strength is listed as it is, rather than failing
// the whole list; opening that game reports the fault.
public sealed record GameSummary(
    Guid Id,
    Side PlayerSide,
    OpponentKind OpponentKind,
    int? OpponentStrength,
    GameProgress Status,
    GameResult? Result,
    GameEndReason? EndReason,
    int HalfMoveCount,
    int HintCount,
    DateTimeOffset UpdatedAt);

/// <summary>One page of a player's games, and where the next one starts.</summary>
/// <param name="Games">The games, most recent activity first.</param>
/// <param name="Next">Where the next page starts; <c>null</c> when this is the last page.</param>
public sealed record GameSummaryPage(IReadOnlyList<GameSummary> Games, GameListCursor? Next);
