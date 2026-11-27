using System.Diagnostics;
using ChessAscent.Api.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>Check-out and return, replacement, and stopping, with the pool built directly around the fake engine.</summary>
public sealed class EnginePoolTests
{
    private const string Kind = "fake";
    private static readonly UciPosition Start = UciPosition.StartingPosition([]);
    private static readonly SearchLimit Depth1 = SearchLimit.Depth(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentCallersAreServedOneAtATimePerProcess()
    {
        using var fake = new FakeEngineKind("--search-ms", "200");
        await using var pool = StartPool(fake.Options(processes: 2), out _);
        var busy = new Dictionary<int, int>();
        var mostAtOnce = new Dictionary<int, int>();
        var gate = new Lock();

        var callers = Enumerable.Range(0, 6).Select(_ => pool.RunAsync(Kind, async lease =>
        {
            var processId = lease.Engine.ProcessId;
            lock (gate)
            {
                busy[processId] = busy.GetValueOrDefault(processId) + 1;
                mostAtOnce[processId] = Math.Max(mostAtOnce.GetValueOrDefault(processId), busy[processId]);
            }

            var result = await lease.Engine.SearchAsync(Start, Depth1, Token);
            lock (gate)
            {
                busy[processId]--;
            }

            return result;
        }, Token));

        var results = await Task.WhenAll(callers);

        results.Should().AllSatisfy(result => result.Value.Should().Be(new BestMove("e2e4")));
        mostAtOnce.Should().HaveCount(2, "both processes served callers");
        mostAtOnce.Values.Should().AllBeEquivalentTo(1, "no process served two callers at once");
    }

    [Fact]
    public async Task KilledIdleEngineIsReplacedAndTheNextRequestSucceeds()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out var logger);
        var first = await ProcessIdAsync(pool);

        KillFromOutside(first);
        (await FakeEngine.IsGoneAsync(first)).Should().BeTrue();

        var second = await pool.RunAsync(Kind, async lease =>
        {
            (await lease.Engine.SearchAsync(Start, Depth1, Token)).Value.Should().Be(new BestMove("e2e4"));
            return lease.Engine.ProcessId;
        }, Token);

        second.Should().NotBe(first);
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains($"replacing process {first}", StringComparison.Ordinal));
        await Eventually.TrueAsync(() => pool.KindsWithoutWorkingEngine().Count == 0, "the kind to be ready again");
    }

    [Fact]
    public async Task EngineKilledWhileIdleIsReplacedWithoutACaller()
    {
        // Readiness must recover even if no request comes to replace the engine.
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);
        var first = (await fake.WaitForStartedAsync(1)).Single();
        await Eventually.TrueAsync(() => pool.KindsWithoutWorkingEngine().Count == 0, "the first engine to be ready");

        KillFromOutside(first);

        await fake.WaitForStartedAsync(2);
        await Eventually.TrueAsync(() => pool.KindsWithoutWorkingEngine().Count == 0, "the replacement to be ready");
    }

    [Fact]
    public async Task EngineThatDiesDuringAConversationIsReplaced()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);

        var killed = 0;

        var dying = () => pool.RunAsync(Kind, async lease =>
        {
            killed = lease.Engine.ProcessId;
            KillFromOutside(killed);
            (await FakeEngine.IsGoneAsync(killed)).Should().BeTrue();
            return await lease.Engine.SearchAsync(Start, Depth1, Token);
        }, Token);

        await dying.Should().ThrowExactlyAsync<UciEngineExitedException>();
        (await ProcessIdAsync(pool)).Should().NotBe(killed);
    }

    [Fact]
    public async Task EngineReportedBrokenIsNotHandedOutAgain()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);

        var broken = await pool.RunAsync(Kind, lease =>
        {
            lease.ReportBroken();
            return Task.FromResult(lease.Engine.ProcessId);
        }, Token);
        var next = await ProcessIdAsync(pool);

        next.Should().NotBe(broken);
        (await FakeEngine.IsGoneAsync(broken)).Should().BeTrue();
    }

    [Fact]
    public async Task EngineGoesBackToThePoolWhenTheConversationThrows()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);
        var first = await ProcessIdAsync(pool);

        var throwing = () => pool.RunAsync<int>(Kind, _ => throw new InvalidOperationException("the caller's own failure"), Token);

        await throwing.Should().ThrowExactlyAsync<InvalidOperationException>();
        (await ProcessIdAsync(pool)).Should().Be(first);
    }

    [Fact]
    public async Task EngineGoesBackToThePoolWhenTheConversationIsCancelled()
    {
        using var fake = new FakeEngineKind("--search-ms", "30000,0");
        await using var pool = StartPool(fake.Options(), out _);
        var first = await ProcessIdAsync(pool);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var cancelled = () => pool.RunAsync(Kind, lease => lease.Engine.SearchAsync(Start, Depth1, cancel.Token), Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await ProcessIdAsync(pool)).Should().Be(first);
    }

    [Fact]
    public async Task LeaseCannotBeUsedAfterTheConversation()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);

        var lease = await pool.RunAsync(Kind, Task.FromResult, Token);
        var engine = () => lease.Engine;

        engine.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public async Task WaitingForAnEngineEndsWhenTheCallerCancels()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);
        var (holder, release) = await HoldTheEngineAsync(pool);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var waiting = () => pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), cancel.Token);

        await waiting.Should().ThrowAsync<OperationCanceledException>();
        release.SetResult(0);
        await holder;
    }

    [Fact]
    public async Task WaitingForAnEngineEndsAtTheCheckOutTimeout()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(checkOutTimeout: TimeSpan.FromMilliseconds(500)), out _);
        var (holder, release) = await HoldTheEngineAsync(pool);
        var clock = Stopwatch.StartNew();

        var waiting = () => pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), Token);

        (await waiting.Should().ThrowExactlyAsync<EngineUnavailableException>()).Which.Message.Should().Contain("within 500 ms");
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(450)).And.BeLessThan(TimeSpan.FromSeconds(5));
        release.SetResult(0);
        await holder;
    }

    [Fact]
    public async Task ProgramThatCannotStartIsTriedTheConfiguredNumberOfTimesWithAPause()
    {
        using var fake = new FakeEngineKind();
        var missing = Path.Combine(Path.GetTempPath(), "no-such-engine-" + Guid.NewGuid());
        var clock = Stopwatch.StartNew();
        await using var pool = StartPool(fake.Options(executablePath: missing, startAttempts: 3, restartDelay: TimeSpan.FromMilliseconds(300)), out var logger);

        await Eventually.TrueAsync(() => logger.Entries.Any(entry => entry.Level == LogLevel.Error), "the pool to give up");

        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(600), "two pauses come between three attempts");
        logger.Entries.Count(entry => entry.Message.Contains("to start a process failed", StringComparison.Ordinal)).Should().Be(3);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error && entry.Message.Contains("gave up after 3 failures", StringComparison.Ordinal));
        pool.KindsWithoutWorkingEngine().Should().Equal(Kind);

        // With every slot given up, a caller is refused at once rather than at the check-out timeout.
        var waiting = () => pool.RunAsync(Kind, lease => Task.FromResult(0), Token);
        (await waiting.Should().ThrowExactlyAsync<EngineUnavailableException>()).Which.Message.Should().Contain("stopped trying");
    }

    [Fact]
    public async Task EngineThatFailsItsCheckIsNotHandedOutAndCountsAsAFailure()
    {
        // Each process answers the handshake's isready, then no other.
        using var fake = new FakeEngineKind("--silent-on", "isready", "--silent-after", "1");
        await using var pool = StartPool(
            fake.Options(startAttempts: 2, readyTimeout: TimeSpan.FromMilliseconds(300), checkOutTimeout: TimeSpan.FromSeconds(20)),
            out var logger);

        var call = () => pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), Token);

        (await call.Should().ThrowExactlyAsync<EngineUnavailableException>()).Which.Message.Should().Contain("stopped trying");
        logger.Entries.Count(entry => entry.Message.Contains("because it failed its check", StringComparison.Ordinal)).Should().Be(2);
        logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("gave up after 2 failures", StringComparison.Ordinal));
        foreach (var processId in fake.StartedProcessIds())
        {
            (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ReadinessListsAKindUntilItHasAWorkingProcess()
    {
        using var fake = new FakeEngineKind();
        await using var pool = BuildPool(fake.Options(), out _);

        pool.KindsWithoutWorkingEngine().Should().Equal(Kind);
        pool.Start();
        await ProcessIdAsync(pool);

        pool.KindsWithoutWorkingEngine().Should().BeEmpty();
    }

    [Fact]
    public async Task StopLeavesNoEngineProcess()
    {
        // One kind obeys quit; the other ignores it and has to be killed.
        using var obeys = new FakeEngineKind();
        using var ignores = new FakeEngineKind("--ignore-quit");
        var options = new EnginePoolOptions();
        options.Kinds["obeys"] = obeys.Options(processes: 2);
        options.Kinds["ignores"] = ignores.Options(processes: 2, exitTimeout: TimeSpan.FromMilliseconds(500));
        await using var pool = new EnginePool(Options.Create(options), new ListLogger<EnginePool>(), new ListLogger<UciEngine>());
        pool.Start();
        var processIds = (await obeys.WaitForStartedAsync(2)).Concat(await ignores.WaitForStartedAsync(2)).ToArray();
        await Eventually.TrueAsync(() => pool.KindsWithoutWorkingEngine().Count == 0, "every kind to be ready");
        var clock = Stopwatch.StartNew();

        await pool.StopAsync();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        foreach (var processId in processIds)
        {
            (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue($"process {processId} was stopped");
        }
    }

    [Fact]
    public async Task StopEndsConversationsAndWaitsInProgress()
    {
        using var fake = new FakeEngineKind("--search-ms", "30000");
        await using var pool = StartPool(fake.Options(), out _);
        var processId = await ProcessIdAsync(pool);
        var searching = pool.RunAsync(Kind, lease => lease.Engine.SearchAsync(Start, Depth1, Token), Token);
        var waiting = pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), Token);
        await Task.Delay(300, Token);

        await pool.StopAsync();

        var awaitSearching = () => searching;
        var awaitWaiting = () => waiting;
        await awaitSearching.Should().ThrowExactlyAsync<ObjectDisposedException>();
        (await awaitWaiting.Should().ThrowExactlyAsync<EngineUnavailableException>()).Which.Message.Should().Contain("stopping");
        (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessThatIsStartingWhenStopBeginsIsStoppedToo()
    {
        // The handshake never finishes, so the pool is still starting the process when it stops.
        using var fake = new FakeEngineKind("--silent-on", "uci");
        await using var pool = StartPool(fake.Options(), out _);
        var processId = (await fake.WaitForStartedAsync(1)).Single();

        await pool.StopAsync();

        (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownKindIsRefused()
    {
        using var fake = new FakeEngineKind();
        await using var pool = BuildPool(fake.Options(), out _);

        var call = () => pool.RunAsync("other", lease => Task.FromResult(0), Token);

        await call.Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public async Task KindNamesIgnoreCase()
    {
        using var fake = new FakeEngineKind();
        await using var pool = StartPool(fake.Options(), out _);

        var processId = await pool.RunAsync("FAKE", lease => Task.FromResult(lease.Engine.ProcessId), Token);

        processId.Should().BePositive();
    }

    private static EnginePool BuildPool(EngineKindOptions kind, out ListLogger<EnginePool> logger)
    {
        var options = new EnginePoolOptions();
        options.Kinds[Kind] = kind;
        logger = new ListLogger<EnginePool>();
        return new EnginePool(Options.Create(options), logger, new ListLogger<UciEngine>());
    }

    private static EnginePool StartPool(EngineKindOptions kind, out ListLogger<EnginePool> logger)
    {
        var pool = BuildPool(kind, out logger);
        pool.Start();
        return pool;
    }

    // Starts a conversation that keeps the pool's only engine until the
    // returned source is completed.
    private static async Task<(Task<int> Holder, TaskCompletionSource<int> Release)> HoldTheEngineAsync(EnginePool pool)
    {
        var holding = new TaskCompletionSource();
        var release = new TaskCompletionSource<int>();
        var holder = pool.RunAsync(Kind, _ =>
        {
            holding.SetResult();
            return release.Task;
        }, Token);
        await holding.Task.WaitAsync(Token);
        return (holder, release);
    }

    private static Task<int> ProcessIdAsync(EnginePool pool) =>
        pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), Token);

    private static void KillFromOutside(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill();
    }
}
