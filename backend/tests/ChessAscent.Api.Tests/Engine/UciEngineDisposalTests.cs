using System.Diagnostics;
using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>Disposal sends quit, kills what does not exit, and leaves no process behind.</summary>
public sealed class UciEngineDisposalTests
{
    private static readonly UciPosition Start = UciPosition.StartingPosition([]);
    private static readonly SearchLimit Depth1 = SearchLimit.Depth(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DisposalSendsQuitAndTheProcessExits()
    {
        using var fake = new FakeEngine();
        var engine = await fake.StartAsync();
        var processId = engine.ProcessId;

        await engine.DisposeAsync();

        (await fake.TranscriptAsync()).Should().EndWith("quit");
        (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
        fake.Logger.Entries.Should().NotContain(entry => entry.Message.Contains("killed", StringComparison.Ordinal));
        engine.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task EngineThatIgnoresQuitIsKilled()
    {
        using var fake = new FakeEngine("--ignore-quit");
        fake.Options = fake.Options with { ExitTimeout = TimeSpan.FromMilliseconds(500) };
        var engine = await fake.StartAsync();
        var processId = engine.ProcessId;
        var clock = Stopwatch.StartNew();

        await engine.DisposeAsync();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
        fake.Logger.Entries.Should().Contain(entry => entry.Message.Contains("killed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposalKillsTheTreeOfAnEngineThatIgnoresQuit()
    {
        // The child inherits the engine's pipes, as a program started by a
        // wrapper script would, and ignores quit with its parent.
        using var fake = FakeEngine.WithChildProcess("--ignore-quit");
        fake.Options = fake.Options with { ExitTimeout = TimeSpan.FromMilliseconds(500) };
        var engine = await fake.StartAsync();
        var childId = await fake.ReadChildPidAsync();
        (await FakeEngine.IsGoneAsync(childId, attempts: 1)).Should().BeFalse();

        await engine.DisposeAsync();

        (await FakeEngine.IsGoneAsync(engine.ProcessId)).Should().BeTrue();
        (await FakeEngine.IsGoneAsync(childId)).Should().BeTrue();
    }

    [Fact]
    public async Task DisposalReportsAChildThatOutlivesTheEngine()
    {
        // The engine obeys quit, but its child goes on running with the
        // engine's output open. Once the engine has gone the child is out of
        // its process tree and cannot be reached; disposal still finishes.
        using var fake = FakeEngine.WithChildProcess();
        fake.Options = fake.Options with { ExitTimeout = TimeSpan.FromMilliseconds(500) };
        var engine = await fake.StartAsync();
        var clock = Stopwatch.StartNew();

        await engine.DisposeAsync();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await FakeEngine.IsGoneAsync(engine.ProcessId)).Should().BeTrue();
        fake.Logger.Entries.Should().Contain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error
            && entry.Message.Contains("still holds its output open", StringComparison.Ordinal));
        fake.Logger.Entries.Should().NotContain(entry => entry.Message.Contains("did not finish", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposalIsSafeTwiceAndConcurrently()
    {
        using var fake = new FakeEngine();
        var engine = await fake.StartAsync();

        await Task.WhenAll(engine.DisposeAsync().AsTask(), engine.DisposeAsync().AsTask());
        await engine.DisposeAsync();

        (await fake.TranscriptAsync()).Count(line => line == "quit").Should().Be(1);
    }

    [Fact]
    public async Task DisposalIsSafeAfterTheProcessHasExited()
    {
        using var fake = new FakeEngine("--exit-on", "go");
        var engine = await fake.StartAsync();
        var search = () => engine.SearchAsync(Start, Depth1, Token);
        await search.Should().ThrowExactlyAsync<UciEngineExitedException>();

        var dispose = async () => await engine.DisposeAsync();

        await dispose.Should().NotThrowAsync();
        (await FakeEngine.IsGoneAsync(engine.ProcessId)).Should().BeTrue();
    }

    [Fact]
    public async Task DisposalDuringASearchEndsItAndLaterCallsAreRefused()
    {
        using var fake = new FakeEngine("--search-ms", "30000");
        var engine = await fake.StartAsync();
        var search = engine.SearchAsync(Start, Depth1, Token);
        await Task.Delay(200, Token);

        await engine.DisposeAsync();

        var awaitSearch = () => search;
        await awaitSearch.Should().ThrowExactlyAsync<ObjectDisposedException>();
        (await fake.TranscriptAsync()).Should().EndWith(["stop", "quit"]);
        (await FakeEngine.IsGoneAsync(engine.ProcessId)).Should().BeTrue();

        var later = () => engine.SearchAsync(Start, Depth1, Token);
        await later.Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task AwaitUsingDisposesAtTheEndOfTheScope()
    {
        using var fake = new FakeEngine();
        int processId;
        await using (var engine = await fake.StartAsync())
        {
            processId = engine.ProcessId;
        }

        (await FakeEngine.IsGoneAsync(processId)).Should().BeTrue();
    }
}
