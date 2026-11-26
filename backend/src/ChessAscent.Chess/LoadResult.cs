namespace ChessAscent.Chess;

/// <summary>The outcome of <see cref="IChessRules.Load"/>.</summary>
public union LoadResult(GameLoaded, InvalidStartingPosition, InvalidMoveInHistory)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>The game was rebuilt.</summary>
/// <param name="Game">The game, after every move was played.</param>
public readonly record struct GameLoaded(IChessGame Game);

/// <summary>
/// The text is not a valid six-field FEN for a legal standard-chess position,
/// or is longer than 255 characters. That is the longest value a PGN tag can
/// hold, so every game that loads can also be exported; a real position's FEN
/// is under 100 characters.
/// </summary>
/// <param name="Fen">The text as given.</param>
public readonly record struct InvalidStartingPosition(string Fen);

/// <summary>A move in the list could not be played.</summary>
/// <param name="Index">The move's zero-based position in the list.</param>
/// <param name="Rejection">Why it was refused.</param>
public readonly record struct InvalidMoveInHistory(int Index, MoveRejection Rejection);
