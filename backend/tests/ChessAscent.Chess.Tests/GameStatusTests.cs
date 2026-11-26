namespace ChessAscent.Chess.Tests;

public sealed class GameStatusTests
{
    [Fact]
    public void NewGameIsOngoingWithWhiteToMove()
    {
        var game = Games.Rules.NewGame();

        game.Status.Value.Should().Be(new Ongoing());
        game.SideToMove.Should().Be(Side.White);
        game.Fen.Should().Be(Games.StartingFen);
        game.History.Should().BeEmpty();
    }

    [Fact]
    public void FoolsMateIsCheckmateWonByBlack()
    {
        var game = Games.Play(Games.FoolsMate[..3]);

        var result = game.TryPlay("d8h4");

        result.Value.Should().Be(new MovePlayed(new PlayedMove("d8h4", "Qh4#"), new Checkmate(Side.Black)));
        game.Status.Value.Should().Be(new Checkmate(Side.Black));
        game.SideToMove.Should().Be(Side.White, "the mated side is the side to move");
    }

    [Fact]
    public void MatedPositionLoadedFromFenIsAlreadyCheckmate()
    {
        var game = Games.Load("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        game.Status.Value.Should().Be(new Checkmate(Side.Black));
    }

    [Fact]
    public void StalemateEndsTheGame()
    {
        var game = Games.Load("7k/8/8/6Q1/8/8/8/K7 w - - 0 1");

        var result = game.TryPlay("g5g6");

        result.Value.Should().Be(new MovePlayed(new PlayedMove("g5g6", "Qg6"), new Stalemate()));
        game.LegalMoves().Should().BeEmpty();
    }

    [Fact]
    public void ThirdOccurrenceOfAPositionDrawsRightAfterTheMoveThatMakesIt()
    {
        var game = Games.Play(Games.KnightDance[..7]);
        game.Status.Value.Should().Be(new Ongoing(), "the starting position has occurred only twice so far");

        var result = game.TryPlay("f6g8");

        result.Value.Should().Be(new MovePlayed(new PlayedMove("f6g8", "Ng8"), new Draw(DrawReason.ThreefoldRepetition)));
        game.History.Should().HaveCount(8, "the move that completes the repetition is played and recorded");
        game.Fen.Should().Be(Games.StartingFen.Replace(" 0 1", " 8 5", StringComparison.Ordinal));
        game.LegalMoves().Should().BeEmpty();
    }

    [Fact]
    public void RepetitionsNeedNotBeConsecutive()
    {
        // The starting position recurs at plies 4 and 10, with other positions in between.
        var game = Games.Load(
            "4k3/8/8/8/8/8/8/R3K3 w - - 0 1",
            "e1d1", "e8d8", "d1e1", "d8e8", "e1d1", "e8d8", "d1d2", "d8d7", "d2e1", "d7e8");

        game.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
    }

    [Fact]
    public void RepetitionIsTheReasonWhenItCoincidesWithTheFiftyMoveRule()
    {
        // The eighth knight move is both the hundredth half-move and the third
        // occurrence of the starting position.
        var game = Games.Load(
            "4k3/8/8/8/8/8/8/R3K1N1 w - - 92 60",
            "g1f3", "e8d8", "f3g1", "d8e8", "g1f3", "e8d8", "f3g1", "d8e8");

        game.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
    }

    [Fact]
    public void HundredthHalfMoveWithoutCaptureOrPawnMoveDrawsByTheFiftyMoveRule()
    {
        var game = Games.Load("4k3/8/8/8/8/8/8/R3K3 w - - 99 60");

        var result = game.TryPlay("a1a2");

        result.Value.Should().Be(new MovePlayed(new PlayedMove("a1a2", "Ra2"), new Draw(DrawReason.FiftyMoveRule)));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(149)]
    [InlineData(150)]
    [InlineData(5000)]
    public void PositionLoadedWithAHalfMoveClockOfAHundredOrMoreIsAlreadyDrawn(int halfMoveClock)
    {
        var game = Games.Load($"4k3/8/8/8/8/8/8/R3K3 w - - {halfMoveClock} 80");

        game.Status.Value.Should().Be(new Draw(DrawReason.FiftyMoveRule));
        game.LegalMoves().Should().BeEmpty();
    }

    [Fact]
    public void CheckmateOnTheHundredthHalfMoveIsCheckmateNotADraw()
    {
        var game = Games.Load("7k/8/6K1/8/8/8/8/R7 w - - 99 60");

        game.TryPlay("a1a8");

        game.Status.Value.Should().Be(new Checkmate(Side.White));
    }

    [Fact]
    public void CapturingTheLastPieceLeavesADeadPosition()
    {
        var game = Games.Load("4k3/8/8/8/8/8/3r4/4K3 w - - 0 1");

        var result = game.TryPlay("e1d2");

        result.Value.Should().Be(new MovePlayed(new PlayedMove("e1d2", "Kxd2"), new Draw(DrawReason.DeadPosition)));
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/2B1K3 w - - 0 1")] // King and bishop against king.
    [InlineData("4k3/8/8/8/8/8/8/1N2K3 w - - 0 1")] // King and knight against king.
    [InlineData("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1")] // Bishops on the same square colour.
    public void DeadPositionLoadedFromFenIsAlreadyDrawn(string fen)
    {
        Games.Load(fen).Status.Value.Should().Be(new Draw(DrawReason.DeadPosition));
    }

    // Whether this is a draw is an open question for the project owner; until
    // it is answered, play continues, as FIDE rules say.
    [Fact]
    public void KingAndTwoKnightsAgainstALoneKingPlaysOn()
    {
        Games.Load("4k3/8/8/8/8/8/8/1N2K1N1 w - - 0 1").Status.Value.Should().Be(new Ongoing());
    }

    // Dead-position detection covers missing material only. Here every pawn is
    // blocked and neither king can reach an enemy pawn, so mate is impossible,
    // but play continues until repetition or the fifty-move rule. Whether to
    // detect such positions is an open question for the project owner.
    [Fact]
    public void LockedPawnsAreNotDetectedAsADeadPosition()
    {
        Games.Load("8/4k3/8/p1p1p1p1/P1P1P1P1/8/4K3/8 w - - 0 1").Status.Value.Should().Be(new Ongoing());
    }

    // The compiler checks this switch for exhaustiveness: adding a case to
    // GameStatus without handling it here fails the build.
    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void EveryStatusCaseCanBeDescribed(string fen, string[] moves, string expected)
    {
        var description = Games.Load(fen, moves).Status switch
        {
            Ongoing => "ongoing",
            Checkmate mate => $"checkmate, {mate.Winner} wins",
            Stalemate => "stalemate",
            Draw draw => $"draw, {draw.Reason}",
        };

        description.Should().Be(expected);
    }

    public static TheoryData<string, string[], string> EveryStatus() => new()
    {
        { Games.StartingFen, [], "ongoing" },
        { Games.StartingFen, Games.FoolsMate, "checkmate, Black wins" },
        { "7k/8/6Q1/8/8/8/8/K7 b - - 0 1", [], "stalemate" },
        { Games.StartingFen, Games.KnightDance, "draw, ThreefoldRepetition" },
        { "4k3/8/8/8/8/8/8/R3K3 w - - 100 60", [], "draw, FiftyMoveRule" },
        { "4k3/8/8/8/8/8/8/4K3 w - - 0 1", [], "draw, DeadPosition" },
    };
}
