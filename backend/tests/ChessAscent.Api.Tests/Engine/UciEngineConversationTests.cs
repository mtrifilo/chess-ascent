using ChessAscent.Api.Engine;
using Microsoft.Extensions.Logging;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>The conversation when the engine behaves: handshake, options and searches.</summary>
public sealed class UciEngineConversationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartCompletesTheHandshake()
    {
        using var fake = new FakeEngine();

        await using (var engine = await fake.StartAsync())
        {
            engine.IsUsable.Should().BeTrue();
        }

        (await fake.TranscriptAsync()).Should().Equal("uci", "isready", "quit");
    }

    [Fact]
    public async Task NoiseBeforeUciokIsIgnored()
    {
        // Blank and white-space lines, near misses such as "uciokay" and
        // "info string uciok", control characters and a line over the length
        // limit all come before the real "uciok".
        using var fake = new FakeEngine("--noise");

        await using var engine = await fake.StartAsync();

        engine.IsUsable.Should().BeTrue();
        (await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token))
            .Value.Should().Be(new BestMove("e2e4"));
        fake.Logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("longer than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetOptionSendsTheOptionAndWaitsForReadyok()
    {
        using var fake = new FakeEngine();
        await using (var engine = await fake.StartAsync())
        {
            await engine.SetOptionAsync("Skill Level", "5", Token);
            await engine.SetOptionAsync("Clear Hash", null, Token);
        }

        (await fake.TranscriptAsync()).Should().Equal(
            "uci", "isready",
            "setoption name Skill Level value 5", "isready",
            "setoption name Clear Hash", "isready",
            "quit");
    }

    [Fact]
    public async Task SearchSendsThePositionAndLimitAndReturnsTheMove()
    {
        using var fake = new FakeEngine();
        await using (var engine = await fake.StartAsync())
        {
            var result = await engine.SearchAsync(UciPosition.StartingPosition(["e2e4", "e7e5"]), SearchLimit.Depth(8), Token);

            result.Value.Should().Be(new BestMove("e2e4"));
        }

        (await fake.TranscriptAsync()).Should().Equal(
            "uci", "isready", "position startpos moves e2e4 e7e5", "go depth 8", "quit");
    }

    [Fact]
    public async Task SearchFromAFenWithANodeLimit()
    {
        const string fen = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";
        using var fake = new FakeEngine();
        await using (var engine = await fake.StartAsync())
        {
            await engine.SearchAsync(UciPosition.FromFen(fen, ["e2e4"]), SearchLimit.Nodes(1000), Token);
            await engine.SearchAsync(UciPosition.FromFen(fen, []), SearchLimit.Nodes(1), Token);
        }

        (await fake.TranscriptAsync()).Should().Equal(
            "uci", "isready",
            $"position fen {fen} moves e2e4", "go nodes 1000",
            $"position fen {fen}", "go nodes 1",
            "quit");
    }

    [Theory]
    [InlineData("(none)")]
    [InlineData("0000")]
    [InlineData("(none) ponder (none)")]
    public async Task NoLegalMoveIsItsOwnResult(string reply)
    {
        using var fake = new FakeEngine("--bestmove", reply);
        await using var engine = await fake.StartAsync();

        var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);

        result.Value.Should().Be(new NoLegalMove());
        engine.IsUsable.Should().BeTrue();
    }

    [Theory]
    [InlineData("e7e8q ponder e1e2", "e7e8q")]
    [InlineData("\t g1f3   ponder\t b8c6 ", "g1f3")]
    [InlineData("a2a4", "a2a4")]
    [InlineData("whatever-the-engine-says", "whatever-the-engine-says")]
    public async Task BestMoveIsTheTokenAfterBestmove(string reply, string move)
    {
        using var fake = new FakeEngine("--bestmove", reply);
        await using var engine = await fake.StartAsync();

        var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);

        result.Value.Should().Be(new BestMove(move));
    }

    [Fact]
    public async Task BestmoveWithNoMoveIsAProtocolErrorAndTheEngineStaysUsable()
    {
        using var fake = new FakeEngine("--bestmove", "");
        await using var engine = await fake.StartAsync();

        var search = () => engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);

        await search.Should().ThrowExactlyAsync<UciProtocolException>();
        engine.IsUsable.Should().BeTrue();
        await engine.SetOptionAsync("Skill Level", "1", Token);
    }

    [Fact]
    public async Task LinesPrintedBetweenCallsAreNotTakenAsTheNextReply()
    {
        using var fake = new FakeEngine("--extra-bestmove", "h2h4");
        await using var engine = await fake.StartAsync();

        (await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token)).Value.Should().Be(new BestMove("e2e4"));

        // Time for the stray line to be read from the pipe before the next call.
        await Task.Delay(300, Token);
        (await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token)).Value.Should().Be(new BestMove("e2e4"));
    }

    [Fact]
    public async Task CrLfLineEndingsAreRead()
    {
        using var fake = new FakeEngine("--crlf", "--noise");
        await using var engine = await fake.StartAsync();

        var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);

        result.Value.Should().Be(new BestMove("e2e4"));
    }

    [Fact]
    public async Task LongLinesAreReadAndOverlongOnesSkipped()
    {
        // 60,000 characters is under the limit; 3,000,000 is far over it, and
        // spans many reads of the pipe.
        using var fake = new FakeEngine("--long-line", "60000", "--long-line", "3000000", "--long-line", "10");
        await using var engine = await fake.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);
            result.Value.Should().Be(new BestMove("e2e4"));
        }

        fake.Logger.Entries.Count(entry => entry.Message.Contains("longer than", StringComparison.Ordinal)).Should().Be(3);
    }

    [Fact]
    public async Task InfoStringLinesAreNeverReplies()
    {
        // During each search the fake prints "info string bestmove a1a1"
        // before its real bestmove.
        using var fake = new FakeEngine();
        await using var engine = await fake.StartAsync();

        var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);

        result.Value.Should().Be(new BestMove("e2e4"));
        fake.Logger.Entries.Should().Contain(entry => entry.Message.Contains("info string bestmove a1a1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StandardErrorGoesToTheLogAndIsNeverParsed()
    {
        using var fake = new FakeEngine("--stderr", "uciok", "--stderr", "bestmove h7h8", "--stderr", "esc \u001b[31m");
        await using var engine = await fake.StartAsync();

        var result = await engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token);
        await engine.DisposeAsync();

        result.Value.Should().Be(new BestMove("e2e4"));
        var errors = fake.Logger.Entries.Where(entry => entry.Message.Contains("stderr", StringComparison.Ordinal)).Select(entry => entry.Message).ToList();
        errors.Should().Contain(message => message.Contains("bestmove h7h8", StringComparison.Ordinal));
        errors.Should().Contain(message => message.Contains("esc ?[31m", StringComparison.Ordinal), "control characters are replaced before logging");
    }

    [Fact]
    public async Task ConcurrentCallsAreServedOneAtATime()
    {
        using var fake = new FakeEngine("--search-ms", "150");
        await using (var engine = await fake.StartAsync())
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var searches = Enumerable.Range(0, 4)
                .Select(i => engine.SearchAsync(UciPosition.StartingPosition([$"m{i}"]), SearchLimit.Depth(i + 1), Token))
                .ToList();
            var option = engine.SetOptionAsync("Skill Level", "3", Token);

            var results = await Task.WhenAll(searches);
            await option;

            results.Select(result => result.Value).Should().AllBeEquivalentTo(new BestMove("e2e4"));

            // Four searches of 150 ms each, one after another.
            clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(600));
        }

        // Each search's position is followed at once by its own go: no
        // command from another call came between them.
        var transcript = await fake.TranscriptAsync();
        transcript.Count(line => line.StartsWith("position", StringComparison.Ordinal)).Should().Be(4);
        for (var i = 0; i < transcript.Length; i++)
        {
            if (transcript[i].StartsWith("position startpos moves m", StringComparison.Ordinal))
            {
                var n = int.Parse(transcript[i][^1..], System.Globalization.CultureInfo.InvariantCulture);
                transcript[i + 1].Should().Be($"go depth {n + 1}");
            }
        }
    }
}
