namespace ChessAscent.Chess.Tests;

public sealed class SquareTests
{
    [Theory]
    [InlineData("a1", 0, 0)]
    [InlineData("e4", 4, 3)]
    [InlineData("h8", 7, 7)]
    public void NameParsesToFileAndRankAndBack(string name, int file, int rank)
    {
        Square.TryParse(name, out var square).Should().BeTrue();

        square.File.Should().Be(file);
        square.Rank.Should().Be(rank);
        square.ToString().Should().Be(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("E4")]
    [InlineData("i1")]
    [InlineData("a0")]
    [InlineData("a9")]
    [InlineData("e4 ")]
    [InlineData("e44")]
    public void AnythingElseIsRefused(string? text)
    {
        Square.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void DefaultSquareIsA1()
    {
        default(Square).ToString().Should().Be("a1");
    }
}
