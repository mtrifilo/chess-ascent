namespace ChessAscent.Chess;

// LEARN: A positional record struct: the parameter list declares the
// properties, the constructor and value equality in one line, as a Java record
// does, but the result is a value type (see Square). `readonly` makes the
// generated properties init-only: they can be set while the value is being
// created, including by a `with` expression that copies it with one property
// changed, and never afterwards. Without `readonly` a positional record
// struct gets ordinary settable properties, unlike a Java record.
/// <summary>A move that has been played.</summary>
/// <param name="Uci">The move in UCI coordinate form, such as <c>e2e4</c> or <c>e7e8q</c>.</param>
/// <param name="San">The move in standard algebraic notation, such as <c>e4</c> or <c>e8=Q+</c>.</param>
public readonly record struct PlayedMove(string Uci, string San);

// LEARN: `PromotionPiece?` on an enum (a value type) is shorthand for
// Nullable<PromotionPiece>, a real wrapper struct with HasValue and Value,
// closer to Java's Optional than to a nullable reference. `string?` (see
// Square.TryParse) is only an annotation on the same type. The two spell the
// same way but are different mechanisms.
/// <summary>A move that may be played in the current position.</summary>
/// <param name="Uci">The move in UCI coordinate form, which <see cref="IChessGame.TryPlay"/> accepts.</param>
/// <param name="From">The square the piece leaves.</param>
/// <param name="To">The square the piece arrives on. For castling, the king's destination.</param>
/// <param name="Promotion">The piece a pawn becomes, or <c>null</c> when the move is not a promotion.</param>
/// <param name="San">The move in standard algebraic notation.</param>
public readonly record struct LegalMove(string Uci, Square From, Square To, PromotionPiece? Promotion, string San);
