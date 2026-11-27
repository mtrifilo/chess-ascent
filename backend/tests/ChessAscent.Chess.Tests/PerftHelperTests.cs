namespace ChessAscent.Chess.Tests;

// The perft helper is itself part of the acceptance gate, so its own
// behaviour is pinned here.
public sealed class PerftHelperTests
{
    [Fact]
    public void DivideSplitsTheCountByFirstMoveInUciOrder()
    {
        var divide = Perft.Divide(Games.Rules.NewGame(), 2);

        divide.Should().HaveCount(20).And.OnlyContain(line => line.Count == 20, "every first move has twenty replies");
        divide.Select(line => line.Uci).Should().BeInAscendingOrder(StringComparer.Ordinal);
        divide.Sum(line => line.Count).Should().Be(Perft.Count(Games.Rules.NewGame(), 2));
    }

    [Fact]
    public void FormatFollowsStockfishsLayout()
    {
        var text = Perft.Format(Perft.Divide(Games.Load("k7/8/8/8/8/8/P7/K7 w - - 0 1"), 1));

        text.Should().Be("a1b1: 1\na1b2: 1\na2a3: 1\na2a4: 1\n\nNodes searched: 4\n");
    }

    [Fact]
    public void FinishedGameHasOnePositionAtDepthZeroAndNoneBeyond()
    {
        var mated = Games.Play(Games.FoolsMate);

        Perft.Count(mated, 0).Should().Be(1);
        Perft.Count(mated, 1).Should().Be(0);
    }

    // King takes the last rook, a dead position. Published counts would go on
    // counting the king moves after it; the interface lists none, so the
    // helper refuses rather than report a count that only looks wrong.
    [Fact]
    public void DrawInsideTheTreeIsReportedInsteadOfCounted()
    {
        var game = Games.Load("4k3/8/8/8/8/8/3r4/4K3 w - - 0 1");

        Perft.Count(game, 1).Should().Be(2, "at depth 1 every move is a leaf, drawn or not");
        var act = () => Perft.Count(game, 2);
        act.Should().Throw<InvalidOperationException>().WithMessage("*DeadPosition*");
    }
}
