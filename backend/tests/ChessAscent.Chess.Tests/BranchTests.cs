namespace ChessAscent.Chess.Tests;

public sealed class BranchTests
{
    [Fact]
    public void BranchAndOriginalMoveIndependently()
    {
        var game = Games.Play("e2e4");

        var branch = game.Branch();
        branch.TryPlay("e7e5");
        game.TryPlay("c7c5");

        game.History.Select(move => move.Uci).Should().Equal("e2e4", "c7c5");
        branch.History.Select(move => move.Uci).Should().Equal("e2e4", "e7e5");
        game.PositionsAfterMoves.Should().Equal(Games.Play("e2e4").Fen, game.Fen);
        branch.PositionsAfterMoves.Should().Equal(Games.Play("e2e4").Fen, branch.Fen);
    }

    [Fact]
    public void BranchOfAFinishedGameIsFinishedToo()
    {
        var branch = Games.Play(Games.KnightDance).Branch();

        branch.Status.Value.Should().Be(new Draw(DrawReason.ThreefoldRepetition));
        branch.TryPlay("g1f3").Value.Should().Be(new GameAlreadyOver(new Draw(DrawReason.ThreefoldRepetition)));
    }

    [Fact]
    public void HistoryIsASnapshot()
    {
        var game = Games.Play("e2e4");
        var history = game.History;

        game.TryPlay("e7e5");

        history.Should().HaveCount(1);
    }

    [Fact]
    public void PositionsAfterMovesIsASnapshot()
    {
        var game = Games.Play("e2e4");
        var positions = game.PositionsAfterMoves;

        game.TryPlay("e7e5");

        positions.Should().HaveCount(1);
    }
}
