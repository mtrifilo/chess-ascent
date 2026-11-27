namespace ChessAscent.Api.Features.Games;

/// <summary>A row of the Moves table: one half-move of one game.</summary>
internal sealed class Move
{
    public Guid GameId { get; set; }

    /// <summary>
    /// The move's place in the game's own move list, from 1 for the first
    /// move played from the starting position, whichever side made it. It is
    /// not the FEN's move number.
    /// </summary>
    public int HalfMoveNumber { get; set; }

    /// <summary>The move in UCI coordinate form, such as <c>e2e4</c> or <c>e7e8q</c>.</summary>
    public required string Uci { get; set; }

    /// <summary>The move in standard algebraic notation, as the chess interface wrote it when it was played.</summary>
    public required string San { get; set; }
}
