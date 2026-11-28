using System.Globalization;
using System.Text.Unicode;
// LEARN: A using alias names a namespace (or a type) locally, like a short
// import name Java does not offer. Every library type in this file is written
// `Realms.Something`, which shows at a glance where the library is touched and
// avoids clashes with this project's own Square.
using Realms = ChessRealms.Engine;

namespace ChessAscent.Chess;

// LEARN: `internal` means visible anywhere in this assembly (this project) and
// nowhere else. It is the closest thing to Java's package-private, but scoped
// to the compiled assembly rather than the package, and a Java module's
// unexported packages are the nearer match. It is what hides the rules
// library: only this class and ChessRules touch it, and nothing outside the
// project can even name this class. Callers see IChessGame.
//
// The library's game object is mutable and documented as not thread-safe,
// and some of its reads write to internal caches; that is why IChessGame
// states its one-instance-per-request rule.
internal sealed class ChessRealmsGame : IChessGame
{
    // Seven characters fit any SAN move (such as "Qa1xb2#"); the rest is margin.
    private const int SanBufferLength = 16;

    /// <summary>The longest value a PGN tag may hold, in UTF-16 code units.</summary>
    internal const int MaxPgnTagValueLength = 255;

    private readonly Realms.ChessGame game;

    // The library records moves but not their SAN, which must be formatted
    // before each move is played, and keeps only the current position, so
    // each move is kept here with both notations and the position after it,
    // written down as it is played. One entry per move keeps the two
    // properties built from them in step.
    //
    // LEARN: (PlayedMove Move, string PositionAfter) is a value tuple: an
    // unnamed struct of two fields, written inline, where Java would need a
    // small record or a Map.Entry. The element names exist only for the
    // compiler, so entry.Move reads well here while the value itself is just
    // a pair; a type that crosses an API boundary should be a record instead.
    private readonly List<(PlayedMove Move, string PositionAfter)> history;

    private ChessRealmsGame(Realms.ChessGame game, List<(PlayedMove Move, string PositionAfter)> history)
    {
        this.game = game;
        this.history = history;
    }

    /// <summary>Wraps a newly created library game, ending it at once if a draw rule already applies.</summary>
    internal static ChessRealmsGame Start(Realms.ChessGame game)
    {
        var wrapped = new ChessRealmsGame(game, []);
        wrapped.EndByDrawRuleIfAvailable();
        return wrapped;
    }

    public string Fen => game.ToFen();

    public Side SideToMove => ToSide(game.CurrentColor);

    public GameStatus Status => ToStatus(game.Outcome);

    public IReadOnlyList<PlayedMove> History => [.. history.Select(entry => entry.Move)];

    public IReadOnlyList<string> PositionsAfterMoves => [.. history.Select(entry => entry.PositionAfter)];

    public IReadOnlyList<LegalMove> LegalMoves() => Describe(game.GetLegalMoves());

    public IReadOnlyList<LegalMove> LegalMovesFrom(Square square)
    {
        return Describe(game.GetLegalMoves().Where(move => ToSquare(move.Source) == square));
    }

    public PlayResult TryPlay(string uciMove)
    {
        ArgumentNullException.ThrowIfNull(uciMove);
        if (!Realms.CoordinateMove.TryParse(uciMove, out var move))
        {
            return new MalformedMove(uciMove);
        }

        if (game.IsFinished)
        {
            return new GameAlreadyOver(Status);
        }

        // SAN describes the move in the position before it, so it is formatted
        // first. Formatting fails exactly when the move is not legal here.
        Span<char> sanBuffer = stackalloc char[SanBufferLength];
        if (!game.TryFormatSan(move, sanBuffer, out var sanLength))
        {
            return new IllegalMove(uciMove);
        }

        var san = new string(sanBuffer[..sanLength]);
        if (game.MakeMove(move) == Realms.MoveResult.None)
        {
            throw new InvalidOperationException($"The rules library formatted {move} as {san} and then refused to play it.");
        }

        var played = new PlayedMove(move.ToString(), san);
        history.Add((played, game.ToFen()));
        EndByDrawRuleIfAvailable();
        return new MovePlayed(played, Status);
    }

    public IChessGame Branch() => new ChessRealmsGame(game.Clone(), [.. history]);

    public PgnExportResult ToPgn(PgnTags tags, DeclaredResult? declaredResult)
    {
        ArgumentNullException.ThrowIfNull(tags);

        // LEARN: `is { } declared` is a property pattern with no properties: it
        // matches any non-null value and binds it, unwrapping the nullable in
        // one step. Java would write `if (opt.isPresent()) { var d = opt.get(); }`.
        Realms.PgnResult? declaredPgnResult = null;
        if (declaredResult is { } declared)
        {
            declaredPgnResult = ToPgnResult(declared);

            // The library throws ArgumentException for a declared result that
            // contradicts a finished game, so the contradiction is caught here
            // and returned as a value.
            if (game.IsFinished && declaredPgnResult != ToPgnResult(game.Outcome.Result))
            {
                return new DeclaredResultContradictsBoard(declared, Status);
            }
        }

        KeyValuePair<string, string?>[] supplied =
        [
            new("Event", tags.Event),
            new("Site", tags.Site),
            new("Date", tags.Date?.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)),
            new("Round", tags.Round),
            new("White", tags.White),
            new("Black", tags.Black),
        ];

        var tagValues = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in supplied)
        {
            if (value is null)
            {
                continue;
            }

            if (!IsValidPgnTagValue(value))
            {
                return new InvalidPgnTagValue(name);
            }

            tagValues.Add(new(name, value));
        }

        // A non-standard starting position is also written as a tag (FEN). It
        // always fits, because ChessRules.Load refuses a FEN longer than a tag
        // value may be, and the library's canonical form is never longer than
        // the text it parsed.
        var document = Realms.PgnGame.FromGame(game, tagValues, declaredPgnResult);
        return new PgnExported(document.ToPgn());
    }

    // Draw rule: threefold repetition and the fifty-move rule end the
    // game automatically. The library treats both as claims a player may make,
    // so the claim is made here, on the position just reached, as soon as one
    // is available: immediately after the move that completes the repetition
    // or the hundredth half-move, and on loading a position whose half-move
    // clock is already 100 or more. That move stays played and recorded.
    // Checkmate or stalemate on the same move takes precedence because the
    // library has already ended the game, and a finished game offers no
    // claims. When both claims are available at once, repetition is recorded
    // as the reason; either ends the game the same way.
    private void EndByDrawRuleIfAvailable()
    {
        var available = game.AvailableDrawClaims;
        var claim = available.HasFlag(Realms.DrawClaim.ThreefoldRepetition) ? Realms.DrawClaim.ThreefoldRepetition
            : available.HasFlag(Realms.DrawClaim.FiftyMoveRule) ? Realms.DrawClaim.FiftyMoveRule
            : Realms.DrawClaim.None;

        if (claim != Realms.DrawClaim.None && !game.ClaimDraw(claim))
        {
            throw new InvalidOperationException($"The library offered a {claim} claim and then refused it.");
        }
    }

    private LegalMove[] Describe(IEnumerable<Realms.CoordinateMove> moves) =>
        moves
            .Select(move => new LegalMove(
                move.ToString(),
                ToSquare(move.Source),
                ToSquare(move.Target),
                ToPromotionPiece(move.Promotion),
                game.FormatSan(move)))
            .OrderBy(move => move.Uci, StringComparer.Ordinal)
            .ToArray();

    // King and two knights against a lone king is not a dead position under
    // FIDE rules, because mate is possible if the defender blunders, and the
    // library plays on. Some sites call it a draw. Which this app wants is an
    // open question for the project owner; until it is answered, play continues.
    private static GameStatus ToStatus(Realms.GameOutcome outcome) => outcome.Reason switch
    {
        Realms.FinishReason.None => new Ongoing(),
        Realms.FinishReason.Checkmate => new Checkmate(ToSide(outcome.Winner)),
        Realms.FinishReason.Stalemate => new Stalemate(),
        Realms.FinishReason.DeadPosition => new Draw(DrawReason.DeadPosition),
        Realms.FinishReason.ThreefoldRepetition => new Draw(DrawReason.ThreefoldRepetition),
        Realms.FinishReason.FiftyMoveRule => new Draw(DrawReason.FiftyMoveRule),

        // The library ends the game by itself at the fifth repetition and the
        // 150th half-move. With the claims above made at the third and the
        // 100th, play never reaches either. A position loaded with a half-move
        // clock of 150 or more arrives already ended by the 75-move rule, which
        // under this app's rules is a fifty-move draw.
        Realms.FinishReason.FivefoldRepetition => new Draw(DrawReason.ThreefoldRepetition),
        Realms.FinishReason.SeventyFiveMoveRule => new Draw(DrawReason.FiftyMoveRule),

        // The library's enum is not closed, so a later version could add a
        // reason; that must fail loudly rather than be guessed at.
        _ => throw new InvalidOperationException($"Unknown finish reason from the rules library: {outcome.Reason}."),
    };

    private static Side ToSide(Realms.PieceColor color) => color switch
    {
        Realms.PieceColor.White => Side.White,
        Realms.PieceColor.Black => Side.Black,
        _ => throw new InvalidOperationException($"The rules library reported no side ({color})."),
    };

    private static Square ToSquare(Realms.Square square) =>
        Square.TryParse(square.ToString(), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The rules library produced an invalid square: {square}.");

    private static PromotionPiece? ToPromotionPiece(Realms.PieceValue piece) => piece switch
    {
        Realms.PieceValue.None => null,
        Realms.PieceValue.Queen => PromotionPiece.Queen,
        Realms.PieceValue.Rook => PromotionPiece.Rook,
        Realms.PieceValue.Bishop => PromotionPiece.Bishop,
        Realms.PieceValue.Knight => PromotionPiece.Knight,
        _ => throw new InvalidOperationException($"The rules library produced an invalid promotion: {piece}."),
    };

    private static Realms.PgnResult ToPgnResult(DeclaredResult result) => result switch
    {
        DeclaredResult.WhiteWins => Realms.PgnResult.WhiteWin,
        DeclaredResult.BlackWins => Realms.PgnResult.BlackWin,
        DeclaredResult.Draw => Realms.PgnResult.Draw,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Not a declared result."),
    };

    private static Realms.PgnResult ToPgnResult(Realms.GameResult result) => result switch
    {
        Realms.GameResult.WhiteWin => Realms.PgnResult.WhiteWin,
        Realms.GameResult.BlackWin => Realms.PgnResult.BlackWin,
        Realms.GameResult.Draw => Realms.PgnResult.Draw,
        Realms.GameResult.Ongoing => Realms.PgnResult.Unknown,
        _ => throw new InvalidOperationException($"Unknown game result from the rules library: {result}."),
    };

    // The PGN tag-value rule as the library applies it: at most 255 UTF-16
    // code units, with no control characters, no line or paragraph separators
    // and no unpaired surrogates. Checking first lets a bad value come back as
    // a result instead of the library's ArgumentException.
    private static bool IsValidPgnTagValue(string value)
    {
        return value.Length <= MaxPgnTagValueLength
            && Utf16.IsValid(value)
            && !value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');
    }
}
