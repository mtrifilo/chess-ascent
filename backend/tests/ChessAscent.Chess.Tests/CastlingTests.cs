namespace ChessAscent.Chess.Tests;

// Castling rights depend on history, which perft from a fixed position does
// not exercise: a right is lost for good once the king or that rook moves, or
// the rook is captured on its square. Castling itself, castling out of check
// and the king-onto-rook form are in TryPlayTests and LegalMovesTests.
public sealed class CastlingTests
{
    // A library that only asks "has a move ever started on h1?" misses this:
    // the h1 rook is captured where it stands, and a different rook later
    // moves onto h1 beside the unmoved king.
    [Fact]
    public void RookCapturedOnItsSquareAndReplacedByAnotherRookGivesNoCastling()
    {
        var game = Games.Load("4k3/8/8/8/8/8/5Rb1/4K2R b K - 0 1", "g2h1");
        game.Fen.Should().Be("4k3/8/8/8/8/8/5R2/4K2b w - - 0 2", "capturing the rook on h1 removes the right at once");

        Games.PlayOn(game, "f2f1", "e8d8", "f1h1", "d8e8");

        game.Fen.Should().Be("4k3/8/8/8/8/8/8/4K2R w - - 1 4");
        game.TryPlay("e1g1").Value.Should().Be(new IllegalMove("e1g1"));
    }

    [Fact]
    public void RookThatMovesAwayAndBackGivesNoCastling()
    {
        var game = Games.Load("4k3/8/8/8/8/8/8/4K2R w K - 0 1", "h1h2", "e8d8", "h2h1", "d8e8");

        game.Fen.Should().Be("4k3/8/8/8/8/8/8/4K2R w - - 4 3");
        game.TryPlay("e1g1").Value.Should().Be(new IllegalMove("e1g1"));
    }

    [Fact]
    public void KingThatMovesAwayAndBackGivesNoCastlingOnEitherSide()
    {
        var game = Games.Load("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1d1", "e8d8", "d1e1", "d8e8");

        game.Fen.Should().Be("r3k2r/8/8/8/8/8/8/R3K2R w - - 4 3");
        game.LegalMovesFrom(Games.SquareNamed("e1")).Select(move => move.Uci).Should().NotContain(["e1g1", "e1c1"]);
    }

    // The king may not pass through an attacked square, but on the queenside
    // the rook may: b1 is attacked here and O-O-O is still legal. f1 is
    // attacked, so O-O is not.
    [Fact]
    public void OnlyTheSquaresTheKingCrossesMustBeSafe()
    {
        var moves = Games.Load("1r3rk1/8/8/8/8/8/8/R3K2R w KQ - 0 1").LegalMovesFrom(Games.SquareNamed("e1"));

        moves.Select(move => move.Uci).Should().Contain("e1c1").And.NotContain("e1g1");
    }
}
