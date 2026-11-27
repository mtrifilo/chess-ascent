using System.ComponentModel.DataAnnotations;
using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

// The enumerations in these bodies (Side, PromotionPiece and those in
// GameValues.cs) are written as the camel-case names of their members (see
// JsonServices), so renaming a member changes the API as well as what the
// Games table stores.

// LEARN: PlayerSide is a nullable enum (Side?) with [Required], where a
// Java DTO would have a plain enum field and @NotNull. A C# enum is a value
// type and can never be null, so a body that left the field out would read
// as Side.White, its zero value, and pass every check. As Side? it reads as
// null, and [Required] refuses that. Strings and records are reference
// types, which are null when missing, so [Required] alone does for them.
/// <summary>The body of <c>POST /games</c>.</summary>
/// <param name="PlayerSide">The side the signed-in player has.</param>
/// <param name="Opponent">Who plays the other side.</param>
public sealed record CreateGameRequest(
    [property: Required] Side? PlayerSide,
    [property: Required] OpponentRequest Opponent);

/// <summary>Who plays the side the player does not.</summary>
/// <param name="Kind">
/// The kind of opponent. Only <c>none</c> is offered, meaning the signed-in
/// player moves both sides; any other is refused with 400.
/// </param>
public sealed record OpponentRequest([property: Required] OpponentKind? Kind);

/// <summary>The body of <c>POST /games/{id}/moves</c>.</summary>
/// <param name="Uci">
/// The move in UCI coordinate form: the square the piece leaves and the
/// square it arrives on, in lower case, such as <c>e2e4</c>, and for a
/// promotion the piece's letter, as in <c>e7e8q</c>. A promotion needs its
/// letter. Castling is the king's move, such as <c>e1g1</c>.
/// </param>
public sealed record PlayMoveRequest([property: Required, MaxLength(GameLimits.UciLength)] string Uci);

public static class GameLimits
{
    /// <summary>
    /// The longest move in UCI form: two squares and a promotion letter, as
    /// in <c>e7e8q</c>. Validation refuses anything longer before the chess
    /// interface reads it.
    /// </summary>
    public const int UciLength = 5;
}

/// <summary>A game as the game screen shows it.</summary>
/// <param name="Id">The game's id.</param>
/// <param name="PlayerSide">The side the signed-in player has.</param>
/// <param name="Opponent">Who plays the other side.</param>
/// <param name="Fen">The current position as six-field FEN.</param>
/// <param name="SideToMove">The side whose turn it is. After checkmate, the side that was mated.</param>
/// <param name="Status">Whether the game is still being played.</param>
/// <param name="Result">How the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="EndReason">Why the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="Moves">The moves played so far, oldest first.</param>
/// <param name="LegalMoves">
/// The squares each piece of the side to move can go to, keyed by the
/// square it stands on, such as <c>{ "g1": ["f3", "h3"] }</c>. A piece with
/// no legal move has no key. A castling move is listed as the king's
/// destination. Empty once the game is over.
/// </param>
/// <param name="Promotions">
/// The moves in <paramref name="LegalMoves"/> that promote a pawn, each with
/// the pieces it may become. Such a move is sent with the piece's letter.
/// Empty when there are none, and once the game is over.
/// </param>
public sealed record GameResponse(
    Guid Id,
    Side PlayerSide,
    OpponentResponse Opponent,
    string Fen,
    Side SideToMove,
    GameProgress Status,
    GameResult? Result,
    GameEndReason? EndReason,
    IReadOnlyList<MoveResponse> Moves,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LegalMoves,
    IReadOnlyList<PromotionResponse> Promotions);

/// <summary>Who plays the side the player does not.</summary>
/// <param name="Kind">The kind of opponent.</param>
/// <param name="Strength">The computer's strength setting; <c>null</c> when the kind is <c>none</c>.</param>
public sealed record OpponentResponse(OpponentKind Kind, int? Strength);

/// <summary>A move that has been played.</summary>
/// <param name="HalfMoveNumber">
/// The move's place in the game, from 1 for the first move played, whichever
/// side made it. It is not the FEN's move number.
/// </param>
/// <param name="Uci">The move in UCI coordinate form, such as <c>e2e4</c> or <c>e7e8q</c>.</param>
/// <param name="San">The move in standard algebraic notation, such as <c>e4</c> or <c>e8=Q+</c>.</param>
public sealed record MoveResponse(int HalfMoveNumber, string Uci, string San);

/// <summary>A pawn move to the last rank, which needs the piece the pawn becomes.</summary>
/// <param name="From">The square the pawn leaves.</param>
/// <param name="To">The square it arrives on.</param>
/// <param name="Pieces">
/// The pieces it may become, in the order queen, rook, bishop, knight. The
/// UCI letters are <c>q</c>, <c>r</c>, <c>b</c> and <c>n</c>.
/// </param>
public sealed record PromotionResponse(string From, string To, IReadOnlyList<PromotionPiece> Pieces);
