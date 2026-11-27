namespace ChessAscent.Chess.Tests;

public sealed class EnPassantTests
{
    [Fact]
    public void PawnThatJustMovedTwoSquaresCanBeTakenAsIfItHadMovedOne()
    {
        var game = Games.Play("e2e4", "a7a6", "e4e5", "d7d5");

        game.TryPlay("e5d6").Value.Should().BeOfType<MovePlayed>().Which.Move.San.Should().Be("exd6");

        game.Fen.Should().Be("rnbqkbnr/1pp1pppp/p2P4/8/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 3", "the captured pawn leaves d5");
    }

    [Fact]
    public void TheRightLastsOneMoveOnly()
    {
        var game = Games.Play("e2e4", "a7a6", "e4e5", "d7d5", "g1f3", "g8f6");

        game.TryPlay("e5d6").Value.Should().Be(new IllegalMove("e5d6"));
    }

    // Taking en passant removes two pawns from the fifth rank at once. Here
    // that would open the rank between the white king on a5 and the black
    // rook on h5, so the capture is illegal although neither pawn is pinned
    // on its own.
    [Fact]
    public void CaptureThatWouldExposeTheKingAlongTheRankIsIllegal()
    {
        var game = Games.Load("8/8/8/KPp4r/8/8/8/7k w - c6 0 1");

        game.LegalMovesFrom(Games.SquareNamed("b5")).Select(move => move.Uci).Should().Equal("b5b6");
        game.TryPlay("b5c6").Value.Should().Be(new IllegalMove("b5c6"));
    }
}
