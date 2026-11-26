namespace ChessAscent.Chess.Tests;

public sealed class LoadTests
{
    [Fact]
    public void StartingPositionPlusMovesRebuildsTheGame()
    {
        var game = Games.Play("e2e4", "e7e5", "g1f3");

        game.Fen.Should().Be("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2");
        game.History.Should().Equal(
            new PlayedMove("e2e4", "e4"), new PlayedMove("e7e5", "e5"), new PlayedMove("g1f3", "Nf3"));
        game.SideToMove.Should().Be(Side.Black);
    }

    [Fact]
    public void CustomStartingPositionKeepsItsSideToMoveAndCounters()
    {
        var game = Games.Load("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "e7e5");

        game.Fen.Should().Be("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2");
        game.History.Should().Equal(new PlayedMove("e7e5", "e5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a fen")]
    [InlineData("rnbqkbnr/ppppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")] // Nine pawns on a rank.
    [InlineData("4k2R/8/8/8/8/8/8/4K3 w - - 0 1")] // The side not to move is in check.
    [InlineData("4k3/8/8/8/8/8/8/4K3 w K - 0 1")] // Castling right with no rook.
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1")] // No kings.
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -")] // Four fields.
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 ")] // Trailing space.
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 0")] // Move number zero.
    public void InvalidStartingPositionComesBackAsAValue(string fen)
    {
        Games.Rules.Load(fen, []).Value.Should().Be(new InvalidStartingPosition(fen));
    }

    [Fact]
    public void FenLongerThanAPgnTagValueIsRefused()
    {
        // Valid FEN apart from its length: move counters have no upper bound.
        var fen = "4k3/8/8/8/8/8/8/R3K3 w - - 0 " + new string('9', 10_000);

        Games.Rules.Load(fen, []).Value.Should().Be(new InvalidStartingPosition(fen));
    }

    [Fact]
    public void FenOfExactlyTheLimitLoads()
    {
        const string prefix = "4k3/8/8/8/8/8/8/R3K3 w - - 0 ";
        var fen = prefix + new string('9', 255 - prefix.Length);

        Games.Load(fen).Fen.Should().Be(fen);
    }

    [Fact]
    public void NullArgumentsAreCallerBugsAndThrow()
    {
        var nullFen = () => Games.Rules.Load(null!, []);
        var nullList = () => Games.Rules.Load(Games.StartingFen, null!);
        var nullMove = () => Games.Rules.Load(Games.StartingFen, ["e2e4", null!]);

        nullFen.Should().Throw<ArgumentNullException>();
        nullList.Should().Throw<ArgumentNullException>();
        nullMove.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResultTextNamesTheCase()
    {
        var result = Games.Rules.Load(Games.StartingFen, ["e2e5"]);

        result.ToString().Should().Be("InvalidMoveInHistory { Index = 0, Rejection = IllegalMove { Uci = e2e5 } }");
    }

    [Fact]
    public void IllegalMoveInTheListIsReportedWithItsIndex()
    {
        var result = Games.Rules.Load(Games.StartingFen, ["e2e4", "e7e5", "e4e5"]);

        result.Value.Should().Be(new InvalidMoveInHistory(2, new IllegalMove("e4e5")));
    }

    [Fact]
    public void MalformedMoveInTheListIsReportedWithItsIndex()
    {
        var result = Games.Rules.Load(Games.StartingFen, ["e2e4", "E7E5"]);

        result.Value.Should().Be(new InvalidMoveInHistory(1, new MalformedMove("E7E5")));
    }

    [Fact]
    public void MoveListThatRunsPastCheckmateIsRefusedAtTheFirstExtraMove()
    {
        var result = Games.Rules.Load(Games.StartingFen, [.. Games.FoolsMate, "a2a3"]);

        result.Value.Should().Be(new InvalidMoveInHistory(4, new GameAlreadyOver(new Checkmate(Side.Black))));
    }

    [Fact]
    public void MoveListThatRunsPastAnAutomaticDrawIsRefusedAtTheFirstExtraMove()
    {
        var result = Games.Rules.Load(Games.StartingFen, [.. Games.KnightDance, "g1f3"]);

        result.Value.Should().Be(new InvalidMoveInHistory(8, new GameAlreadyOver(new Draw(DrawReason.ThreefoldRepetition))));
    }

    [Fact]
    public void EachLoadReturnsAnIndependentGame()
    {
        var first = Games.Play("e2e4");
        var second = Games.Play("e2e4");

        first.TryPlay("e7e5");

        second.Should().NotBeSameAs(first);
        second.History.Should().HaveCount(1);
    }
}
