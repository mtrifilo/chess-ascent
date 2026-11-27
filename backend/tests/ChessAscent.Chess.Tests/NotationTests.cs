namespace ChessAscent.Chess.Tests;

// Standard algebraic notation (SAN) as the interface reports it, both for a
// move about to be played (LegalMove.San) and for one already played
// (PlayedMove.San). The two must agree, so every case checks both. The
// notation of a stalemating move, which must carry no suffix, is checked in
// GameStatusTests.StalemateEndsTheGame; promotion with check in
// TryPlayTests.PromotionNeedsItsPieceLetter; en passant in EnPassantTests.
public sealed class NotationTests
{
    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1", "b1d2", "Nbd2")] // Two knights reach d2: the file tells them apart.
    [InlineData("4k3/8/8/R7/8/8/8/R3K3 w - - 0 1", "a1a3", "R1a3")] // Two rooks on the a-file: the rank tells them apart.
    [InlineData("4k3/8/8/8/8/Q7/8/Q1Q1K3 w - - 0 1", "a1b2", "Qa1b2")] // Three queens reach b2: neither alone is enough.
    [InlineData("k3r3/8/8/8/8/2N1N3/8/4K3 w - - 0 1", "c3d5", "Nd5")] // The e3 knight is pinned, so it is not an alternative.
    public void AmbiguousMovesAreDisambiguatedByFileRankOrBoth(string fen, string uci, string san)
    {
        AssertSan(fen, uci, san);
    }

    [Theory]
    [InlineData("rnbqkbnr/ppp1pppp/3p4/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "f1b5", "Bb5+")]
    [InlineData("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "h5f7", "Qxf7#")]
    [InlineData("5k2/8/8/8/8/8/8/4K2R w K - 0 1", "e1g1", "O-O+")]
    [InlineData("3r4/4P1k1/8/8/8/8/8/4K3 w - - 0 1", "e7d8q", "exd8=Q")]
    [InlineData("3r4/4P1k1/8/8/8/8/8/4K3 w - - 0 1", "e7d8n", "exd8=N")]
    public void ChecksMatesCapturesAndPromotionsAreMarked(string fen, string uci, string san)
    {
        AssertSan(fen, uci, san);
    }

    private static void AssertSan(string fen, string uci, string san)
    {
        var game = Games.Load(fen);

        game.LegalMoves().Should().ContainSingle(move => move.Uci == uci).Which.San.Should().Be(san);
        game.TryPlay(uci).Value.Should().BeOfType<MovePlayed>().Which.Move.Should().Be(new PlayedMove(uci, san));
    }
}
