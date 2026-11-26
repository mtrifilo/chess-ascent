using System.ComponentModel;
using System.Diagnostics;
using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>Timeouts, cancellation and an engine that dies.</summary>
public sealed class UciEngineFailureTests
{
    private static readonly UciPosition Start = UciPosition.StartingPosition([]);
    private static readonly SearchLimit Depth1 = SearchLimit.Depth(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandshakeTimesOutAndLeavesNoProcess()
    {
        using var fake = new FakeEngine("--silent-on", "uci");
        fake.Options = fake.Options with { HandshakeTimeout = TimeSpan.FromMilliseconds(1500) };

        var start = fake.StartAsync;

        (await start.Should().ThrowExactlyAsync<UciEngineTimeoutException>()).Which.Awaited.Should().Be("uciok and readyok");
        (await FakeEngine.IsGoneAsync(await fake.ReadPidAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task EngineThatExitsDuringTheHandshakeIsReportedAsExited()
    {
        using var fake = new FakeEngine("--exit-on", "isready", "--exit-code", "7");

        var start = fake.StartAsync;

        (await start.Should().ThrowExactlyAsync<UciEngineExitedException>()).Which.ExitCode.Should().Be(7);
    }

    [Fact]
    public async Task ProgramThatDoesNotExistFailsToStart()
    {
        using var fake = new FakeEngine();
        fake.Options = fake.Options with { ExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-engine-" + Guid.NewGuid()) };

        var start = fake.StartAsync;

        await start.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public async Task CancellingTheHandshakeLeavesNoProcess()
    {
        using var fake = new FakeEngine("--silent-on", "uci");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));

        var start = () => UciEngine.StartAsync(fake.Options, fake.Logger, cancel.Token);

        await start.Should().ThrowAsync<OperationCanceledException>();
        (await FakeEngine.IsGoneAsync(await fake.ReadPidAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task SearchTimeoutSendsStopDiscardsTheReplyAndKeepsTheEngine()
    {
        // The first search would take 30 s; the second answers at once.
        using var fake = new FakeEngine("--search-ms", "30000,0");
        fake.Options = fake.Options with { SearchTimeout = TimeSpan.FromMilliseconds(500) };
        await using var engine = await fake.StartAsync();

        var slow = () => engine.SearchAsync(Start, Depth1, Token);

        (await slow.Should().ThrowExactlyAsync<UciEngineTimeoutException>()).Which.Awaited.Should().Be("bestmove");
        engine.IsUsable.Should().BeTrue();
        (await fake.TranscriptAsync()).Should().EndWith("stop");

        // The fake answers a stopped search with a7a6, which must not be
        // taken as the answer to this one.
        (await engine.SearchAsync(Start, Depth1, Token)).Value.Should().Be(new BestMove("e2e4"));
    }

    [Fact]
    public async Task SearchTimeoutWithNoAnswerToStopMakesTheEngineUnusable()
    {
        using var fake = new FakeEngine("--silent-on", "go");
        fake.Options = fake.Options with
        {
            SearchTimeout = TimeSpan.FromMilliseconds(300),
            StopTimeout = TimeSpan.FromMilliseconds(300),
        };
        await using var engine = await fake.StartAsync();

        var search = () => engine.SearchAsync(Start, Depth1, Token);

        await search.Should().ThrowExactlyAsync<UciEngineTimeoutException>();
        engine.IsUsable.Should().BeFalse();
        await search.Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetOptionTimeoutMakesTheEngineUnusable()
    {
        // The handshake's isready is answered; later ones are not.
        using var fake = new FakeEngine("--silent-on", "isready", "--silent-after", "1");
        fake.Options = fake.Options with { ReadyTimeout = TimeSpan.FromMilliseconds(300) };
        await using var engine = await fake.StartAsync();

        var setOption = () => engine.SetOptionAsync("Skill Level", "1", Token);

        (await setOption.Should().ThrowExactlyAsync<UciEngineTimeoutException>()).Which.Awaited.Should().Be("readyok");
        engine.IsUsable.Should().BeFalse();
        await setOption.Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancellingASearchSendsStopAndDoesNotReturnItsBestmove()
    {
        using var fake = new FakeEngine("--search-ms", "30000,0");
        await using var engine = await fake.StartAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        var search = () => engine.SearchAsync(Start, Depth1, cancel.Token);

        (await search.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancel.Token);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        engine.IsUsable.Should().BeTrue();
        (await fake.TranscriptAsync()).Should().EndWith("stop");
        (await engine.SearchAsync(Start, Depth1, Token)).Value.Should().Be(new BestMove("e2e4"));
    }

    [Fact]
    public async Task CancellingASearchThatIgnoresStopMakesTheEngineUnusable()
    {
        using var fake = new FakeEngine("--silent-on", "go");
        fake.Options = fake.Options with { StopTimeout = TimeSpan.FromMilliseconds(300) };
        await using var engine = await fake.StartAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var search = () => engine.SearchAsync(Start, Depth1, cancel.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
        engine.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenSendsNothing()
    {
        using var fake = new FakeEngine();
        await using var engine = await fake.StartAsync();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var search = () => engine.SearchAsync(Start, Depth1, cancel.Token);
        var setOption = () => engine.SetOptionAsync("Skill Level", "1", cancel.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
        await setOption.Should().ThrowAsync<OperationCanceledException>();
        engine.IsUsable.Should().BeTrue();
        (await fake.TranscriptAsync()).Should().Equal("uci", "isready");
    }

    [Fact]
    public async Task CancellingACallThatIsWaitingItsTurnLeavesTheEngineUsable()
    {
        using var fake = new FakeEngine("--search-ms", "1000,0");
        await using var engine = await fake.StartAsync();
        var first = engine.SearchAsync(Start, Depth1, Token);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var waiting = () => engine.SearchAsync(Start, Depth1, cancel.Token);

        await waiting.Should().ThrowAsync<OperationCanceledException>();
        (await first).Value.Should().Be(new BestMove("e2e4"));
        engine.IsUsable.Should().BeTrue();
    }

    [Fact]
    public async Task EngineThatCrashesMidSearchIsReportedAsExitedNotAsATimeout()
    {
        using var fake = new FakeEngine("--exit-on", "go", "--exit-code", "3");
        await using var engine = await fake.StartAsync();
        var clock = Stopwatch.StartNew();

        var search = () => engine.SearchAsync(Start, Depth1, Token);

        (await search.Should().ThrowExactlyAsync<UciEngineExitedException>()).Which.ExitCode.Should().Be(3);
        clock.Elapsed.Should().BeLessThan(fake.Options.SearchTimeout);
        engine.IsUsable.Should().BeFalse();

        // Every later call says the same.
        await search.Should().ThrowExactlyAsync<UciEngineExitedException>();
        var setOption = () => engine.SetOptionAsync("Skill Level", "1", Token);
        await setOption.Should().ThrowExactlyAsync<UciEngineExitedException>();
    }

    // A process that dies is reported as exited on every path through the
    // client, never as a timeout or as the caller's cancellation, so that
    // whoever owns the engine knows to replace it. Each case names the call
    // and the command the fake dies on.
    [Theory]
    [InlineData("start", "uci")]
    [InlineData("start", "isready")]
    [InlineData("set option", "setoption")]
    [InlineData("search", "position")]
    [InlineData("search", "go")]
    [InlineData("search that times out", "stop")]
    [InlineData("search that is cancelled", "stop")]
    public async Task EngineThatDiesIsReportedAsExitedOnEveryPath(string call, string diesOn)
    {
        using var fake = new FakeEngine("--exit-on", diesOn, "--exit-code", "5", "--search-ms", "30000");
        fake.Options = fake.Options with { SearchTimeout = TimeSpan.FromMilliseconds(300) };
        using var cancel = new CancellationTokenSource();
        UciEngine? engine = null;
        Func<Task> act = call switch
        {
            "start" => async () => engine = await fake.StartAsync(),
            "set option" => async () => await (engine = await fake.StartAsync()).SetOptionAsync("Skill Level", "1", Token),
            "search" or "search that times out" => async () => await (engine = await fake.StartAsync()).SearchAsync(Start, Depth1, Token),
            "search that is cancelled" => async () =>
            {
                engine = await fake.StartAsync();
                cancel.CancelAfter(TimeSpan.FromMilliseconds(100));
                await engine.SearchAsync(Start, Depth1, cancel.Token);
            }
            ,
            _ => throw new ArgumentOutOfRangeException(nameof(call)),
        };

        try
        {
            (await act.Should().ThrowExactlyAsync<UciEngineExitedException>()).Which.ExitCode.Should().Be(5);
            if (engine is not null)
            {
                engine.IsUsable.Should().BeFalse();
            }
        }
        finally
        {
            if (engine is not null)
            {
                await engine.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task EngineKilledFromOutsideIsReportedAsExited()
    {
        using var fake = new FakeEngine("--search-ms", "30000");
        await using var engine = await fake.StartAsync();
        var search = engine.SearchAsync(Start, Depth1, Token);
        await Task.Delay(200, Token);

        using (var process = Process.GetProcessById(engine.ProcessId))
        {
            process.Kill();
        }

        var awaitSearch = () => search;
        var exited = (await awaitSearch.Should().ThrowExactlyAsync<UciEngineExitedException>()).Which;
        if (!OperatingSystem.IsWindows())
        {
            exited.ExitCode.Should().Be(137, "128 plus SIGKILL's number, 9");
        }
    }
}
