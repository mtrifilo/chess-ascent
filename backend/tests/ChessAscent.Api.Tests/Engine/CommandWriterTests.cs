using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>
/// The seam where a command is written. Through a real engine, a timeout
/// that lands between reading one reply and writing the next command is too
/// brief to hit reliably, so the decision is tested here directly.
/// </summary>
public sealed class CommandWriterTests
{
    [Fact]
    public async Task ACommandIsWrittenAsOneLine()
    {
        using var input = new StringWriter { NewLine = "\n" };

        var outcome = await CommandWriter.WriteAsync(input, "isready", TestContext.Current.CancellationToken);

        outcome.Should().Be(CommandWriteOutcome.Written);
        input.ToString().Should().Be("isready\n");
    }

    [Fact]
    public async Task TimeThatRanOutBeforeTheWriteLeavesNothingWritten()
    {
        using var input = new StringWriter { NewLine = "\n" };
        using var expired = new CancellationTokenSource();
        await expired.CancelAsync();

        var outcome = await CommandWriter.WriteAsync(input, "position startpos", expired.Token);

        outcome.Should().Be(CommandWriteOutcome.NotStarted);
        input.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task TimeThatRunsOutDuringTheWriteIsACutOff()
    {
        using var input = new StallingWriter();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var outcome = await CommandWriter.WriteAsync(input, "position startpos", deadline.Token);

        outcome.Should().Be(CommandWriteOutcome.CutOff);
        input.ToString().Should().Be("p", "part of the command was written before the time ran out");
    }

    [Theory]
    [InlineData("bestmove", false, "did not answer with bestmove within 300 ms")]
    [InlineData(null, true, "did not take a command within 300 ms")]
    [InlineData(null, false, "between two commands")]
    public void TheTimeoutMessageSaysWhatRanOut(string? awaited, bool commandCutOff, string expected)
    {
        var timeout = new UciEngineTimeoutException(awaited, TimeSpan.FromMilliseconds(300), commandCutOff);

        timeout.Message.Should().Contain(expected);
        timeout.Awaited.Should().Be(awaited);
    }

    // Writes the first character of a line, then waits until it is cancelled,
    // as a pipe that has filled up would.
    private sealed class StallingWriter : StringWriter
    {
        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span[..1]);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
