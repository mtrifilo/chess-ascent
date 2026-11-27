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

    // A library that compares a position only with the ones exactly 4 and 8
    // half-moves earlier finds the knight dance above and misses both of
    // these. Each row is a move list whose last move makes the third
    // occurrence of the starting position, and no earlier move does.
    public static TheoryData<string, string[]> RepetitionsThatAreNotFourHalfMovesApart() => new()
    {
        // A six-half-move king triangle, twice: the position recurs at plies 6 and 12.
        { "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", ["e1d1", "e8d8", "d1d2", "d8d7", "d2e1", "d7e8", "e1d1", "e8d8", "d1d2", "d8d7", "d2e1", "d7e8"] },

        // Uneven gaps: the position recurs at plies 4 and 10, with other positions in between.
        { "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", ["e1d1", "e8d8", "d1e1", "d8e8", "e1d1", "e8d8", "d1d2", "d8d7", "d2e1", "d7e8"] },
    };

    [Theory]
    [MemberData(nameof(RepetitionsThatAreNotFourHalfMovesApart))]
    public void RepetitionIsFoundWhateverTheGapsBetweenOccurrences(string fen, string[] moves)
    {
        var game = Games.Load(fen, moves[..^1]);
        game.Status.Value.Should().Be(new Ongoing(), "the position has occurred only twice before the last move");

        game.TryPlay(moves[^1]).Value.Should().BeOfType<MovePlayed>()
            .Which.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
    }

    // Positions repeat only if the same moves are possible, so a position
    // with castling rights is not the same as one with the same pieces and
    // none. Here the kingside rooks step aside and back, which removes the
    // kingside rights, then the knights dance. The starting placement recurs
    // without those rights at plies 8, 12 and 16; counting the start, which
    // had them, would wrongly make ply 12 the third occurrence.
    [Fact]
    public void LostCastlingRightsMakeADifferentPosition()
    {
        string[] rooksAsideAndBack = ["g1f3", "g8f6", "h1g1", "h8g8", "g1h1", "g8h8", "f3g1", "f6g8"];
        string[] knightsOutAndBack = ["g1f3", "g8f6", "f3g1", "f6g8"];

        var game = Games.Play([.. rooksAsideAndBack, .. knightsOutAndBack]);
        game.Status.Value.Should().Be(new Ongoing(), "the position at ply 0 had castling rights that the one at ply 12 lacks");

        foreach (var move in knightsOutAndBack)
        {
            game.TryPlay(move);
        }

        game.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
    }

    // An en passant capture that is available makes a position different
    // from the same pieces without it: after e2e4 here Black may take on e3,
    // and once the kings shuffle that chance is gone. The placement after
    // e2e4 recurs without the capture at plies 5, 9 and 13; counting ply 1,
    // which had it, would wrongly make ply 9 the third occurrence.
    [Fact]
    public void AnAvailableEnPassantCaptureMakesADifferentPosition()
    {
        string[] kingsShuffle = ["e8d8", "e1d1", "d8e8", "d1e1"];

        var game = Games.Load("4k3/8/8/8/3p4/8/4P3/4K3 w - - 0 1", ["e2e4", .. kingsShuffle, .. kingsShuffle]);
        game.Status.Value.Should().Be(new Ongoing());

        foreach (var move in kingsShuffle)
        {
            game.TryPlay(move);
        }

        game.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
    }

    // The other side of the same rule: when no pawn can take en passant, the
    // double step leaves the same position as any other way of reaching it,
    // although FEN records an en passant square after every double step. The
    // position after e2e4 recurs at plies 5 and 9, which is the third occurrence.
    [Fact]
    public void AnEnPassantSquareNoPawnCanUseDoesNotMakeADifferentPosition()
    {
        string[] kingsShuffle = ["e8d8", "e1d1", "d8e8", "d1e1"];

        var game = Games.Load("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", ["e2e4", .. kingsShuffle, .. kingsShuffle]);

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

    [Theory]
    [InlineData("4k3/8/8/8/8/8/r7/R3K3 w - - 99 60", "a1a2")] // A capture.
    [InlineData("4k3/8/8/8/8/8/P7/R3K3 w - - 99 60", "a2a3")] // A pawn move.
    public void CaptureOrPawnMoveOnTheHundredthHalfMoveRestartsTheCount(string fen, string uci)
    {
        var game = Games.Load(fen);

        game.TryPlay(uci).Value.Should().BeOfType<MovePlayed>().Which.Status.Value.Should().Be(new Ongoing());
        game.Fen.Should().Contain(" b - - 0 60");
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

    // Mate is possible in each of these, if only with help from the defender,
    // so none is a dead position under FIDE rules.
    [Theory]
    [InlineData("4kn2/8/8/8/8/8/8/2B1K3 w - - 0 1")] // King and bishop against king and knight.
    [InlineData("4k1b1/8/8/8/8/8/8/2B1K3 w - - 0 1")] // Bishops on squares of different colours.
    [InlineData("4kn2/8/8/8/8/8/8/1N2K3 w - - 0 1")] // A knight each.
    [InlineData("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1")] // King and pawn against king.
    public void MaterialThatCanStillMatePlaysOn(string fen)
    {
        Games.Load(fen).Status.Value.Should().Be(new Ongoing());
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
