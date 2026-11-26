namespace ChessAscent.Chess.Tests;

public sealed class TryPlayTests
{
    private const string PromotionFen = "8/4P1k1/8/8/8/8/8/4K3 w - - 0 1";

    [Theory]
    [InlineData("e2e4", "e4")]
    [InlineData("g1f3", "Nf3")]
    public void LegalMoveIsPlayedAndRecordedInBothNotations(string uci, string san)
    {
        var game = Games.Rules.NewGame();

        var result = game.TryPlay(uci);

        result.Value.Should().Be(new MovePlayed(new PlayedMove(uci, san), new Ongoing()));
        game.History.Should().Equal(new PlayedMove(uci, san));
        game.SideToMove.Should().Be(Side.Black);
    }

    [Theory]
    [InlineData("e7e8q", "e8=Q")]
    [InlineData("e7e8r", "e8=R")]
    [InlineData("e7e8b", "e8=B")]
    [InlineData("e7e8n", "e8=N+")]
    public void PromotionNeedsItsPieceLetter(string uci, string san)
    {
        var game = Games.Load(PromotionFen);

        // Only the move is checked: promoting to a bishop or knight here leaves
        // king and minor piece against king, which is a dead position.
        game.TryPlay(uci).Value.Should().BeOfType<MovePlayed>().Which.Move.Should().Be(new PlayedMove(uci, san));
    }

    [Fact]
    public void CastlingIsTheKingsTwoSquareMove()
    {
        var game = Games.Load("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        game.TryPlay("e1g1").Value.Should().Be(new MovePlayed(new PlayedMove("e1g1", "O-O"), new Ongoing()));
        game.Fen.Should().Be("r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1");
    }

    [Theory]
    [InlineData("e2e5")] // Too far for a pawn.
    [InlineData("e3e4")] // No piece on e3.
    [InlineData("e7e5")] // Black's pawn, with White to move.
    [InlineData("e2e4q")] // A promotion letter on a move that is not a promotion.
    [InlineData("e1h1")] // King onto its own rook, a castling form UCI does not use.
    public void IllegalMoveComesBackAsAValueAndLeavesTheGameUnchanged(string uci)
    {
        var game = Games.Rules.NewGame();

        var result = game.TryPlay(uci);

        result.Value.Should().Be(new IllegalMove(uci));
        game.Fen.Should().Be(Games.StartingFen);
        game.History.Should().BeEmpty();
    }

    [Fact]
    public void PromotionWithoutAPieceLetterIsIllegalRatherThanAQueen()
    {
        var game = Games.Load(PromotionFen);

        game.TryPlay("e7e8").Value.Should().Be(new IllegalMove("e7e8"));
        game.Fen.Should().Be(PromotionFen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2")]
    [InlineData("E2E4")]
    [InlineData("e2e4 ")]
    [InlineData(" e2e4")]
    [InlineData("e2-e4")]
    [InlineData("e7e8k")]
    [InlineData("i2i4")]
    [InlineData("e0e1")]
    [InlineData("e2e2")]
    [InlineData("0000")]
    [InlineData("Nf3")]
    public void TextThatIsNotAUciMoveComesBackAsMalformed(string input)
    {
        var game = Games.Rules.NewGame();

        game.TryPlay(input).Value.Should().Be(new MalformedMove(input));
        game.Fen.Should().Be(Games.StartingFen);
    }

    [Fact]
    public void MoveAfterCheckmateComesBackAsGameAlreadyOver()
    {
        var game = Games.Play(Games.FoolsMate);

        var result = game.TryPlay("a2a3");

        result.Value.Should().Be(new GameAlreadyOver(new Checkmate(Side.Black)));
        game.History.Should().HaveCount(4);
    }

    [Fact]
    public void MoveAfterAnAutomaticDrawComesBackAsGameAlreadyOver()
    {
        var game = Games.Play(Games.KnightDance);

        game.TryPlay("g1f3").Value.Should().Be(new GameAlreadyOver(new Draw(DrawReason.ThreefoldRepetition)));
    }

    // Every caller must handle every case: this switch has no default arm, so
    // a new PlayResult case would fail the build here.
    [Fact]
    public void EveryPlayResultCaseCanBeHandled()
    {
        var game = Games.Rules.NewGame();

        string[] moves = ["e2e4", "e7e4", "zz"];
        var outcomes = moves.Select(move => game.TryPlay(move) switch
        {
            MovePlayed played => $"played {played.Move.San}",
            IllegalMove illegal => $"illegal {illegal.Uci}",
            MalformedMove malformed => $"malformed {malformed.Input}",
            GameAlreadyOver over => $"over {over.Status.Value}",
        }).ToArray();

        outcomes.Should().Equal("played e4", "illegal e7e4", "malformed zz");
    }
}
