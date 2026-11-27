using System.Diagnostics;
using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>What an owner uses to tell whether an engine still works: the isready ping, and the task that completes when the process ends.</summary>
public sealed class UciEngineHealthTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PingSendsIsreadyAndWaitsForReadyok()
    {
        using var fake = new FakeEngine();
        await using (var engine = await fake.StartAsync())
        {
            await engine.PingAsync(Token);

            engine.IsUsable.Should().BeTrue();
        }

        (await fake.TranscriptAsync()).Should().Equal("uci", "isready", "isready", "quit");
    }

    [Fact]
    public async Task PingThatIsNotAnsweredTimesOutAndLeavesTheEngineUnusable()
    {
        // The handshake's isready is answered; the ping's is not.
        using var fake = new FakeEngine("--silent-on", "isready", "--silent-after", "1");
        fake.Options = fake.Options with
        {
            ReadyTimeout = TimeSpan.FromMilliseconds(300),
            StopTimeout = TimeSpan.FromMilliseconds(300),
        };
        await using var engine = await fake.StartAsync();

        var ping = () => engine.PingAsync(Token);

        (await ping.Should().ThrowExactlyAsync<UciEngineTimeoutException>()).Which.Awaited.Should().Be("readyok");
        engine.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task PingOfAnEngineThatHasExitedReportsTheExit()
    {
        using var fake = new FakeEngine();
        await using var engine = await fake.StartAsync();
        Kill(engine.ProcessId);
        (await FakeEngine.IsGoneAsync(engine.ProcessId)).Should().BeTrue();

        var ping = () => engine.PingAsync(Token);

        await ping.Should().ThrowExactlyAsync<UciEngineExitedException>();
    }

    [Fact]
    public async Task EndedCompletesWhenTheProcessExits()
    {
        using var fake = new FakeEngine();
        await using var engine = await fake.StartAsync();
        engine.Ended.IsCompleted.Should().BeFalse();

        Kill(engine.ProcessId);

        await engine.Ended.WaitAsync(TimeSpan.FromSeconds(10), Token);
        engine.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task EndedCompletesWhenTheEngineIsDisposed()
    {
        using var fake = new FakeEngine();
        var engine = await fake.StartAsync();

        await engine.DisposeAsync();

        engine.Ended.IsCompleted.Should().BeTrue();
    }

    private static void Kill(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill();
    }
}
