using System.Globalization;
using System.Threading.Channels;

namespace ChessAscent.Api.Engine;

/// <summary>
/// The processes of one kind of engine: keeps each of its slots filled with
/// a running process, hands idle ones to callers one at a time, and replaces
/// those that end or break.
/// </summary>
/// <remarks>
/// Each slot has its own loop (<see cref="RunSlotAsync"/>), which starts a
/// process, puts it in the idle queue, waits until it is retired, stops it
/// and starts the next. Check-out and return only move engines in and out of
/// the queue and retire them; they never start or stop a process.
/// </remarks>
internal sealed partial class EngineKindPool
{
    private readonly UciEngineOptions _engineOptions;
    private readonly string? _randomSeedArgument;
    private readonly TimeSpan _checkOutTimeout;
    private readonly int _startAttempts;
    private readonly TimeSpan _restartDelay;
    private readonly ILogger _logger;
    private readonly ILogger<UciEngine> _engineLogger;
    private readonly CancellationToken _stopping;

    // Completes when stopping begins, for waits that take a Task rather than a token.
    private readonly Task _stopped;

    // Engines that are idle, oldest first, plus entries for engines retired
    // while they waited here, which check-out skips. Unbounded, because those
    // stale entries come on top of at most one live entry per slot.
    private readonly Channel<PooledEngine> _idle = Channel.CreateUnbounded<PooledEngine>();

    // Each slot's current engine, checked out or not, for readiness.
    private readonly PooledEngine?[] _current;

    // Slots that have not given up. When the last one does, the idle queue is
    // closed, so that callers waiting for an engine fail at once.
    private int _slotsTrying;

    public EngineKindPool(string kind, EngineKindOptions options, ILogger logger, ILogger<UciEngine> engineLogger, CancellationToken stopping)
    {
        Kind = kind;
        _engineOptions = options.ToEngineOptions();
        _randomSeedArgument = string.IsNullOrWhiteSpace(options.RandomSeedArgument) ? null : options.RandomSeedArgument;
        _checkOutTimeout = options.CheckOutTimeout!.Value;
        _startAttempts = options.StartAttempts!.Value;
        _restartDelay = options.RestartDelay!.Value;
        _logger = logger;
        _engineLogger = engineLogger;
        _stopping = stopping;
        _stopped = Task.Delay(Timeout.Infinite, stopping);
        _current = new PooledEngine?[options.Processes];
        _slotsTrying = options.Processes;
    }

    public string Kind { get; }

    /// <summary>Whether at least one of this kind's processes is running and usable, idle or not.</summary>
    public bool HasWorkingEngine
    {
        get
        {
            for (var slot = 0; slot < _current.Length; slot++)
            {
                if (Volatile.Read(ref _current[slot]) is { Engine.IsUsable: true })
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Runs every slot until stopping is requested, and returns once each has stopped its process.</summary>
    public Task RunAsync() => Task.WhenAll(Enumerable.Range(0, _current.Length).Select(RunSlotAsync));

    /// <summary>
    /// Waits for an idle engine that passes its check, and takes it. Engines
    /// that fail the check are retired, and the wait goes on. The wait ends
    /// at the check-out timeout or when the caller cancels, even during a
    /// check: the check then goes on without the caller, and decides whether
    /// the engine goes back to the idle queue.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    /// <exception cref="EngineUnavailableException">No engine was ready in time, the kind has none left, or the pool is stopping.</exception>
    public async Task<PooledEngine> CheckOutAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping);
        wait.CancelAfter(_checkOutTimeout);
        try
        {
            while (true)
            {
                var pooled = await _idle.Reader.ReadAsync(wait.Token);
                if (!pooled.TryCheckOut())
                {
                    continue;
                }

                var check = CheckAsync(pooled);
                try
                {
                    if (await check.WaitAsync(wait.Token))
                    {
                        pooled.MarkHandedOut();
                        return pooled;
                    }
                }
                catch (OperationCanceledException)
                {
                    _ = PutBackIfPassedAsync(pooled, check);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unavailable(string.Create(CultureInfo.InvariantCulture, $"no engine was free and ready within {_checkOutTimeout.TotalMilliseconds:0} ms"));
        }
        catch (ChannelClosedException)
        {
            throw Unavailable("none of its processes could be started, and the pool has stopped trying");
        }
    }

    /// <summary>
    /// Takes back an engine at the end of a conversation: to the idle queue
    /// if it is still usable and the caller did not report it broken;
    /// otherwise it is retired.
    /// </summary>
    public void Return(PooledEngine pooled, bool reportedBroken)
    {
        if (reportedBroken)
        {
            pooled.Retire("a caller reported it broken");
        }
        else if (!pooled.Engine.IsUsable)
        {
            pooled.Retire("it became unusable while a caller had it");
        }
        else
        {
            PutBack(pooled);
        }
    }

    private void PutBack(PooledEngine pooled)
    {
        if (pooled.TryReturn())
        {
            _idle.Writer.TryWrite(pooled);

            // The engine may have ended since it was last seen usable. If the
            // slot noticed while it was still checked out, the slot left it
            // to this return, so it is retired here.
            if (!pooled.Engine.IsUsable)
            {
                pooled.RetireIfIdle("it ended while idle");
            }
        }
    }

    // Whether the engine may be handed out. One that has exited is not, nor
    // one that does not answer isready (a process can die a moment before
    // IsUsable shows it); either is retired. The ping is bounded by the ready
    // timeout and by stopping, not by the caller, so that a caller who gives
    // up mid-check is not held up while the engine's reply is collected.
    // Never fails.
    private async Task<bool> CheckAsync(PooledEngine pooled)
    {
        var engine = pooled.Engine;
        if (!engine.IsUsable)
        {
            pooled.Retire("it had ended while idle");
            return false;
        }

        try
        {
            await engine.PingAsync(_stopping);
            return true;
        }
        catch (Exception e) when (e is UciEngineException or InvalidOperationException or OperationCanceledException)
        {
            // InvalidOperationException includes ObjectDisposedException, and
            // OperationCanceledException comes only from stopping: in both
            // cases the pool is stopping the engine, which is not its fault.
            if (!_stopping.IsCancellationRequested)
            {
                LogCheckFailed(_logger, e, Kind, engine.ProcessId);
            }

            pooled.Retire("it failed its check before hand-out");
            return false;
        }
    }

    // The caller stopped waiting during the check; the engine goes back to
    // the idle queue if it passed. CheckAsync never fails, so neither does this.
    private async Task PutBackIfPassedAsync(PooledEngine pooled, Task<bool> check)
    {
        if (await check)
        {
            PutBack(pooled);
        }
    }

    // A slot's whole life: start a process, publish it, wait until it is
    // retired or stopping begins, stop it, and start the next, after a pause.
    // The pause comes before every start but the first, so that nothing
    // restarts faster than once per restart delay, however the engine ends.
    // Failures in a row are counted, and give the slot up at the limit: a
    // failure is a start that fails, or an engine retired before it was ever
    // handed to a caller. One that was handed out resets the count, because
    // it showed the program starts and answers. An engine that ends during
    // a conversation is not counted against the program, because the cause
    // may be that conversation's input: counting it would let a few requests
    // that crash the engine leave the kind with no process until restart.
    private async Task RunSlotAsync(int slot)
    {
        // Process.Start is synchronous, and the first start would otherwise
        // run on the caller's thread: the host's, during start-up.
        await Task.Yield();

        var failures = 0;
        var firstStart = true;
        while (!_stopping.IsCancellationRequested)
        {
            if (!firstStart && !await PauseAsync())
            {
                return;
            }

            firstStart = false;

            var engine = await TryStartAsync(slot, failures + 1);
            if (engine is null)
            {
                failures++;
            }
            else
            {
                var pooled = new PooledEngine(engine);
                Volatile.Write(ref _current[slot], pooled);
                _idle.Writer.TryWrite(pooled);

                var reason = await WaitUntilRetiredAsync(pooled);

                Volatile.Write(ref _current[slot], null);
                await engine.DisposeAsync();
                if (reason is null || _stopping.IsCancellationRequested)
                {
                    return;
                }

                LogReplacing(_logger, Kind, slot, engine.ProcessId, reason);
                failures = pooled.WasHandedOut ? 0 : failures + 1;
            }

            if (!_stopping.IsCancellationRequested && GaveUp(slot, failures))
            {
                return;
            }
        }
    }

    // The reason the engine was retired, or null if stopping began first.
    private async Task<string?> WaitUntilRetiredAsync(PooledEngine pooled)
    {
        // An engine that dies while idle is retired now rather than at the
        // next check-out, so that readiness recovers without waiting for a
        // caller. One that dies while checked out is retired when it is returned.
        var first = await Task.WhenAny(pooled.Retired, pooled.Engine.Ended, _stopped);
        if (first == pooled.Engine.Ended)
        {
            pooled.RetireIfIdle("it ended while idle");
            first = await Task.WhenAny(pooled.Retired, _stopped);
        }

        return first == _stopped ? null : await pooled.Retired;
    }

    private async Task<UciEngine?> TryStartAsync(int slot, int attempt)
    {
        try
        {
            var engine = await UciEngine.StartAsync(OptionsForStart(), _engineLogger, _stopping);
            LogStarted(_logger, Kind, slot, engine.ProcessId);
            return engine;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return null;
        }
        // Any failure to start counts as a failed attempt, whatever its type
        // (a missing program is a Win32Exception, a handshake that times out
        // a UciEngineTimeoutException, and so on): the policy is the same for
        // all, and a slot whose loop ended here would stay empty unnoticed.
        catch (Exception e)
        {
            LogStartFailed(_logger, e, Kind, slot, attempt, _startAttempts);
            return null;
        }
    }

    // The seed is drawn here, at each start, rather than once with the rest
    // of the options, so that no two processes of the kind share one, and a
    // replacement does not repeat its predecessor's choices.
    private UciEngineOptions OptionsForStart() => _randomSeedArgument is null
        ? _engineOptions
        : _engineOptions with
        {
            Arguments = [.. _engineOptions.Arguments, _randomSeedArgument, Random.Shared.Next().ToString(CultureInfo.InvariantCulture)],
        };

    // False if stopping began during the pause.
    private async Task<bool> PauseAsync()
    {
        try
        {
            await Task.Delay(_restartDelay, _stopping);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool GaveUp(int slot, int failures)
    {
        if (failures < _startAttempts)
        {
            return false;
        }

        LogGaveUp(_logger, Kind, slot, failures);
        if (Interlocked.Decrement(ref _slotsTrying) == 0)
        {
            _idle.Writer.TryComplete();
        }

        return true;
    }

    // Stopping explains any failure to get an engine once it has begun.
    private EngineUnavailableException Unavailable(string reason) =>
        new(Kind, _stopping.IsCancellationRequested ? "the engine pool is stopping" : reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine kind {Kind}, slot {Slot}: started process {ProcessId}")]
    private static partial void LogStarted(ILogger logger, string kind, int slot, int processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine kind {Kind}, slot {Slot}: attempt {Attempt} of {Attempts} to start a process failed")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, string kind, int slot, int attempt, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine kind {Kind}, slot {Slot}: gave up after {Failures} failures in a row; the slot stays empty until the application restarts")]
    private static partial void LogGaveUp(ILogger logger, string kind, int slot, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine kind {Kind}, slot {Slot}: replacing process {ProcessId}, because {Reason}")]
    private static partial void LogReplacing(ILogger logger, string kind, int slot, int processId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine kind {Kind}: process {ProcessId} failed its check before hand-out")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception, string kind, int processId);
}
