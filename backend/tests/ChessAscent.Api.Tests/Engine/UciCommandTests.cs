using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>The commands built from the caller's text, and the text they refuse.</summary>
public sealed class UciCommandTests
{
    [Fact]
    public void PositionCommands()
    {
        UciPosition.StartingPosition([]).Command.Should().Be("position startpos");
        UciPosition.StartingPosition(["e2e4", "e7e5"]).Command.Should().Be("position startpos moves e2e4 e7e5");
        UciPosition.FromFen(" 8/8/8/8/8/8/8/K6k w - - 0 1 ", ["a1a2"]).Command.Should().Be("position fen 8/8/8/8/8/8/8/K6k w - - 0 1 moves a1a2");
    }

    [Fact]
    public void LimitCommands()
    {
        SearchLimit.Depth(12).Command.Should().Be("go depth 12");
        SearchLimit.Nodes(5_000_000_000).Command.Should().Be("go nodes 5000000000");
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2e4 e7e5")]
    [InlineData("e2e4\nquit")]
    [InlineData("e2e4\r")]
    [InlineData("e2\u0000e4")]
    public void MoveThatCouldBreakTheCommandIsRefused(string move)
    {
        var build = () => UciPosition.StartingPosition([move]);

        build.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8/8/8/8/8/8/8/K6k w - - 0 1\nquit")]
    [InlineData("8/8/8/8/8/8/8/K6k w - - 0 1 moves e2e4")]
    public void FenThatCouldBreakTheCommandIsRefused(string fen)
    {
        var build = () => UciPosition.FromFen(fen, []);

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LimitsBelowOneAreRefused()
    {
        var depth = () => SearchLimit.Depth(0);
        var nodes = () => SearchLimit.Nodes(-1);

        depth.Should().Throw<ArgumentOutOfRangeException>();
        nodes.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("Skill value Level", "1")]
    [InlineData("name", "1")]
    [InlineData("Skill\nLevel", "1")]
    [InlineData("Skill Level", "1\nquit")]
    public async Task OptionThatCouldBreakTheCommandIsRefusedBeforeAnythingIsSent(string name, string value)
    {
        using var fake = new FakeEngine();
        await using var engine = await fake.StartAsync();

        var set = () => engine.SetOptionAsync(name, value, TestContext.Current.CancellationToken);

        await set.Should().ThrowAsync<ArgumentException>();
        (await fake.TranscriptAsync()).Should().Equal("uci", "isready");
        engine.IsUsable.Should().BeTrue();
    }

    [Fact]
    public async Task OptionsWithoutTimeoutsAreRefused()
    {
        using var fake = new FakeEngine();
        var zero = fake.Options with { StopTimeout = TimeSpan.Zero };
        var huge = fake.Options with { SearchTimeout = TimeSpan.FromDays(30) };

        var startZero = () => UciEngine.StartAsync(zero, fake.Logger, TestContext.Current.CancellationToken);
        var startHuge = () => UciEngine.StartAsync(huge, fake.Logger, TestContext.Current.CancellationToken);

        await startZero.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await startHuge.Should().ThrowAsync<ArgumentOutOfRangeException>();
        File.Exists(fake.PidPath).Should().BeFalse("no process is started");
    }
}
