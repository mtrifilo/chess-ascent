namespace ChessAscent.Chess.Tests;

public sealed class PgnTests
{
    private static readonly PgnTags Tags = new()
    {
        Event = "Casual game",
        Site = "Chess Ascent",
        Date = new DateOnly(2026, 11, 26),
        Round = "1",
        White = "Player",
        Black = "Computer",
    };

    [Fact]
    public void FinishedGameExportsTheSevenTagRosterAndTheBoardsResult()
    {
        var pgn = Export(Games.Play(Games.FoolsMate), Tags, declaredResult: null);

        pgn.Should().Be(
            """
            [Event "Casual game"]
            [Site "Chess Ascent"]
            [Date "2026.11.26"]
            [Round "1"]
            [White "Player"]
            [Black "Computer"]
            [Result "0-1"]

            1. f3 e5 2. g4 Qh4# 0-1


            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MissingTagsAreWrittenAsUnknown()
    {
        var pgn = Export(Games.Rules.NewGame(), new PgnTags(), declaredResult: null);

        pgn.Should().StartWith(
            """
            [Event "?"]
            [Site "?"]
            [Date "????.??.??"]
            [Round "?"]
            [White "?"]
            [Black "?"]
            [Result "*"]
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void DeclaredResultRecordsAResignationInAnOngoingGame()
    {
        var pgn = Export(Games.Play("e2e4"), Tags, DeclaredResult.WhiteWins);

        pgn.Should().Contain("[Result \"1-0\"]").And.Contain("1. e4 1-0");
    }

    [Fact]
    public void AutomaticDrawExportsAsADraw()
    {
        var pgn = Export(Games.Play(Games.KnightDance), Tags, declaredResult: null);

        pgn.Should().Contain("[Result \"1/2-1/2\"]").And.Contain("4. Ng1 Ng8 1/2-1/2");
    }

    [Fact]
    public void DeclaredResultThatMatchesAFinishedGameIsAccepted()
    {
        Export(Games.Play(Games.FoolsMate), Tags, DeclaredResult.BlackWins).Should().Contain("[Result \"0-1\"]");
    }

    [Fact]
    public void DeclaredResultThatContradictsAFinishedGameComesBackAsAValue()
    {
        var result = Games.Play(Games.FoolsMate).ToPgn(Tags, DeclaredResult.WhiteWins);

        result.Value.Should().Be(new DeclaredResultContradictsBoard(DeclaredResult.WhiteWins, new Checkmate(Side.Black)));
    }

    // Attribute arguments are stored as UTF-8, which cannot hold an unpaired
    // surrogate, so these values are built in code rather than in [InlineData].
    public static TheoryData<string> ValuesPgnCannotHold() =>
    [
        "Line\nbreak",
        "Tab\there",
        "Separator\u2028here",
        "Unpaired \ud800 surrogate",
        "Unpaired \udc00 surrogate",
        "Ends in a high surrogate \ud800",
    ];

    [Theory]
    [MemberData(nameof(ValuesPgnCannotHold))]
    public void TagValuePgnCannotHoldComesBackAsAValue(string white)
    {
        var result = Games.Rules.NewGame().ToPgn(Tags with { White = white }, declaredResult: null);

        result.Value.Should().Be(new InvalidPgnTagValue("White"));
    }

    [Fact]
    public void TagValueOfMoreThan255CodeUnitsComesBackAsAValue()
    {
        var result = Games.Rules.NewGame().ToPgn(Tags with { Event = new string('x', 256) }, declaredResult: null);

        result.Value.Should().Be(new InvalidPgnTagValue("Event"));
    }

    [Fact]
    public void TagValuesWithQuotesAndNonAsciiTextAreEscapedOrKept()
    {
        var pgn = Export(Games.Rules.NewGame(), Tags with { White = "Ana \"the Rook\" P\u00e9rez \U0001F451", Black = new string('y', 255) }, null);

        pgn.Should().Contain("[White \"Ana \\\"the Rook\\\" P\u00e9rez \U0001F451\"]");
    }

    [Fact]
    public void CustomStartingPositionIsRecordedAsSetUpAndFen()
    {
        const string fen = "7k/8/8/6Q1/8/8/8/K7 w - - 0 1";
        var game = Games.Load(fen, "g5g6");

        var pgn = Export(game, Tags, declaredResult: null);

        pgn.Should().Contain($"[FEN \"{fen}\"]").And.Contain("[SetUp \"1\"]").And.Contain("1. Qg6 1/2-1/2");
    }

    [Fact]
    public void StartingPositionTooLongForAPgnTagComesBackAsAValue()
    {
        // FEN move counters have no upper bound, so this position is valid but
        // its FEN does not fit in a PGN tag.
        var game = Games.Load("4k3/8/8/8/8/8/8/R3K3 w - - 0 " + new string('9', 300));

        game.ToPgn(Tags, declaredResult: null).Value.Should().Be(new InvalidPgnTagValue("FEN"));
    }

    private static string Export(IChessGame game, PgnTags tags, DeclaredResult? declaredResult) =>
        game.ToPgn(tags, declaredResult) switch
        {
            PgnExported exported => exported.Pgn,
            var other => throw new InvalidOperationException($"Expected PGN, but got {other.Value}."),
        };
}
