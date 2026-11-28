using System.ComponentModel.DataAnnotations;
using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

// The enumerations in these bodies (Side and PromotionPiece from the chess
// interface, and those in GameValues.cs) are written as the camel-case names
// of their members (see JsonServices), so renaming a member changes the API,
// and for the enumerations the Games table stores, the stored rows too.
// ApiDocumentTests lists every name the document allows, so such a rename
// fails a test. PromotionResponse lists pieces in PromotionPiece's declared
// order, so reordering its members changes that order.

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
/// The kind of opponent: <c>computer</c>, or <c>none</c> when the signed-in
/// player moves both sides.
/// </param>
/// <param name="Strength">
/// The computer's strength: 600, 800, 1000, 1200 or 1400, where higher is
/// stronger. Left out or <c>null</c>, it is 800. Any other number is refused
/// with 400, as is a strength given when the kind is <c>none</c>.
/// </param>
// The numbers above are OpponentStrengths.Offered and DefaultStrength,
// written out because this comment becomes the API document's description.
public sealed record OpponentRequest([property: Required] OpponentKind? Kind, int? Strength = null)
{
    /// <summary>The computer's strength when the request gives none.</summary>
    public const int DefaultStrength = 800;
}

/// <summary>The body of <c>POST /games/{id}/moves</c>.</summary>
/// <param name="Uci">
/// The move in UCI coordinate form: the square the piece leaves and the
/// square it arrives on, in lower case, such as <c>e2e4</c>, and for a
/// promotion the piece's letter, as in <c>e7e8q</c>. A promotion needs its
/// letter. Castling is the king's move, such as <c>e1g1</c>.
/// </param>
/// <param name="HalfMoveNumber">
/// The half-move number this move is meant to have, numbered as the
/// response's moves are: one more than the number of moves in the game the
/// client is showing. Any other number, whether the game has moved on since
/// or the number is simply wrong, is refused with 409 before the move is
/// played, and nothing is saved, so sending the same move again is always
/// safe.
/// </param>
public sealed record PlayMoveRequest(
    [property: Required, MaxLength(PlayMoveRequest.UciLength)] string Uci,
    [property: Required] int? HalfMoveNumber)
{
    /// <summary>
    /// The longest move in UCI form: two squares and a promotion letter, as
    /// in <c>e7e8q</c>. Validation refuses anything longer before the chess
    /// interface reads it.
    /// </summary>
    public const int UciLength = 5;
}

/// <summary>The body of <c>POST /games/{id}/hints</c>.</summary>
/// <param name="HalfMoveNumber">
/// The half-move number the hinted move would have, as a move request gives
/// it: one more than the number of moves in the game the client is showing.
/// Any other number is refused with 409 before a hint is looked for, so a
/// hint always belongs to the position the client is showing.
/// </param>
public sealed record HintRequest([property: Required] int? HalfMoveNumber);

/// <summary>
/// The hint: the move the hint engine finds best for the player, which is
/// not played.
/// </summary>
/// <param name="From">The square of the piece to move, such as <c>b1</c>.</param>
/// <param name="To">
/// The square it moves to, such as <c>d2</c>. A castling move is given as the
/// king's move. A move that promotes a pawn is given by its two squares only.
/// </param>
public sealed record HintResponse(string From, string To);

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
/// <param name="HintCount">
/// The number of positions in which the player was given a hint. Asking
/// again in the same position does not add to it. Above 0, the game is one
/// in which a hint was used.
/// </param>
/// <param name="LastActivityAt">
/// When the game was created, last had a move saved, or was resigned, as an
/// ISO 8601 time in UTC. The list of games gives the same time.
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
    IReadOnlyList<PromotionResponse> Promotions,
    int HintCount,
    DateTimeOffset LastActivityAt);

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
/// <param name="Fen">
/// The position after the move, as six-field FEN. The last move's is the
/// game's <c>fen</c>.
/// </param>
public sealed record MoveResponse(int HalfMoveNumber, string Uci, string San, string Fen);

/// <summary>One page of the signed-in player's games, most recent activity first.</summary>
/// <param name="Games">
/// The games, most recent activity first. Games with the same last activity
/// time are in the same order on every request.
/// </param>
/// <param name="NextCursor">
/// Where the next page starts: send it back unchanged as <c>cursor</c>, with
/// the same <c>status</c>, for the next page. <c>null</c> when there are no
/// more games. The next page holds the games after this page's last one as
/// the list stands when it is asked for: a game that has had a move since
/// is at the top of the list instead, where reading the list again finds it,
/// and no game is listed on two pages while the server's clock runs forward.
/// </param>
public sealed record GameListResponse(IReadOnlyList<GameSummaryResponse> Games, string? NextCursor);

/// <summary>A game as the list of games shows it, without its moves.</summary>
/// <param name="Id">The game's id.</param>
/// <param name="PlayerSide">The side the signed-in player has.</param>
/// <param name="Opponent">Who plays the other side.</param>
/// <param name="Status">Whether the game is still being played.</param>
/// <param name="Result">How the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="EndReason">Why the game ended; <c>null</c> while it is ongoing.</param>
/// <param name="HalfMoveCount">
/// The number of half-moves played so far, by both sides: the number of
/// moves the game's own response lists.
/// </param>
/// <param name="HintCount">
/// The number of positions in which the player was given a hint, as the
/// game's own response gives it. Above 0, the game is one in which a hint was
/// used.
/// </param>
/// <param name="LastActivityAt">
/// When the game was created, last had a move saved, or was resigned, as an
/// ISO 8601 time in UTC.
/// </param>
public sealed record GameSummaryResponse(
    Guid Id,
    Side PlayerSide,
    OpponentResponse Opponent,
    GameProgress Status,
    GameResult? Result,
    GameEndReason? EndReason,
    int HalfMoveCount,
    int HintCount,
    DateTimeOffset LastActivityAt);

/// <summary>A pawn move to the last rank, which needs the piece the pawn becomes.</summary>
/// <param name="From">The square the pawn leaves.</param>
/// <param name="To">The square it arrives on.</param>
/// <param name="Pieces">
/// The pieces it may become, in the order queen, rook, bishop, knight. The
/// UCI letters are <c>q</c>, <c>r</c>, <c>b</c> and <c>n</c>.
/// </param>
public sealed record PromotionResponse(string From, string To, IReadOnlyList<PromotionPiece> Pieces);
