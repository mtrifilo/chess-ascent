namespace ChessAscent.Chess.Tests;

public sealed class LegalMovesTests
{
    [Fact]
    public void StartingPositionHasTwentyMovesInUciOrder()
    {
        var moves = Games.Rules.NewGame().LegalMoves();

        moves.Should().HaveCount(20);
        moves.Select(move => move.Uci).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void MovesFromOneSquareCarryBothNotationsAndBothSquares()
    {
        var moves = Games.Rules.NewGame().LegalMovesFrom(Games.SquareNamed("g1"));

        moves.Should().Equal(
            new LegalMove("g1f3", Games.SquareNamed("g1"), Games.SquareNamed("f3"), null, "Nf3"),
            new LegalMove("g1h3", Games.SquareNamed("g1"), Games.SquareNamed("h3"), null, "Nh3"));
    }

    [Theory]
    [InlineData("e4")] // Empty square.
    [InlineData("e7")] // The opponent's piece.
    [InlineData("a1")] // A piece with no move.
    public void SquareWithNoMovablePieceOfTheSideToMoveHasNoMoves(string square)
    {
        Games.Rules.NewGame().LegalMovesFrom(Games.SquareNamed(square)).Should().BeEmpty();
    }

    [Fact]
    public void PromotionIsListedOncePerPiece()
    {
        var moves = Games.Load("8/4P1k1/8/8/8/8/8/4K3 w - - 0 1").LegalMovesFrom(Games.SquareNamed("e7"));

        moves.Select(move => (move.Uci, move.Promotion, move.San)).Should().Equal(
            ("e7e8b", PromotionPiece.Bishop, "e8=B"),
            ("e7e8n", PromotionPiece.Knight, "e8=N+"),
            ("e7e8q", PromotionPiece.Queen, "e8=Q"),
            ("e7e8r", PromotionPiece.Rook, "e8=R"));
    }

    [Fact]
    public void KingInCheckCannotCastle()
    {
        var moves = Games.Load("4r1k1/8/8/8/8/8/8/R3K2R w KQ - 0 1").LegalMovesFrom(Games.SquareNamed("e1"));

        moves.Select(move => move.Uci).Should().Equal("e1d1", "e1d2", "e1f1", "e1f2");
    }

    [Fact]
    public void FinishedGameHasNoMoves()
    {
        var game = Games.Play(Games.FoolsMate);

        game.LegalMoves().Should().BeEmpty();
        game.LegalMovesFrom(Games.SquareNamed("e1")).Should().BeEmpty();
    }
}
