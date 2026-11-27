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
    private readonly string _kind;
    private readonly UciEngineOptions _engineOptions;
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
        _kind = kind;
        _engineOptions = options.ToEngineOptions();
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

    public string Kind => _kind;

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

    /// <summary>Ends every wait for an engine, once stopping has been requested.</summary>
    public void CloseIdleQueue() => _idle.Writer.TryComplete();

    /// <summary>
    /// Waits for an idle engine that passes its check, and takes it. Engines
    /// that fail the check are retired, and the wait goes on.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    /// <exception cref="EngineUnavailableException">No engine was ready in time, the kind has none left, or the pool is stopping.</exception>
    public async Task<PooledEngine> CheckOutAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_checkOutTimeout);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _stopping);
        try
        {
            while (true)
            {
                var pooled = await _idle.Reader.ReadAsync(wait.Token);
                if (pooled.TryCheckOut() && await PassesCheckAsync(pooled, wait.Token))
                {
                    return pooled;
                }
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw _stopping.IsCancellationRequested
                ? Unavailable("the engine pool is stopping")
                : Unavailable(string.Create(CultureInfo.InvariantCulture, $"no engine was free within {_checkOutTimeout.TotalMilliseconds:0} ms"));
        }
        catch (ChannelClosedException)
        {
            throw _stopping.IsCancellationRequested
                ? Unavailable("the engine pool is stopping")
                : Unavailable("none of its processes could be started, and the pool has stopped trying");
        }
    }

    /// <summary>
    /// Gives a checked-out engine back: to the idle queue if it is still
    /// usable and the caller did not report it broken; otherwise it is retired.
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
        else if (pooled.TryReturn())
        {
            _idle.Writer.TryWrite(pooled);
        }
    }

    // An engine that has exited is not handed out, nor one that does not
    // answer isready: a process can die a moment before IsUsable shows it.
    private async Task<bool> PassesCheckAsync(PooledEngine pooled, CancellationToken cancellationToken)
    {
        var engine = pooled.Engine;
        if (!engine.IsUsable)
        {
            pooled.Retire("it had ended while idle");
            return false;
        }

        try
        {
            await engine.PingAsync(cancellationToken);
            pooled.MarkHandedOut();
            return true;
        }
        catch (OperationCanceledException)
        {
            // The caller stopped waiting, which says nothing against the engine.
            Return(pooled, reportedBroken: false);
            throw;
        }
        catch (Exception e) when (e is UciEngineException or InvalidOperationException)
        {
            // InvalidOperationException includes ObjectDisposedException:
            // the pool began stopping the engine.
            LogCheckFailed(_logger, e, _kind, engine.ProcessId);
            pooled.Retire("it failed its check before hand-out");
            return false;
        }
    }

    // A slot's whole life: start a process, publish it, wait until it is
    // retired or stopping begins, stop it, and start the next. Failures in a
    // row are counted, with a pause after each, so a program that crashes on
    // start does not spin; an engine that was handed out at least once
    // resets the count, because it showed the program works.
    private async Task RunSlotAsync(int slot)
    {
        var failures = 0;
        while (!_stopping.IsCancellationRequested)
        {
            if (failures > 0 && !await PauseAsync())
            {
                return;
            }

            var engine = await TryStartAsync(slot, failures + 1);
            if (engine is null)
            {
                if (_stopping.IsCancellationRequested || GaveUp(slot, ++failures))
                {
                    return;
                }

                continue;
            }

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

            LogReplacing(_logger, _kind, slot, engine.ProcessId, reason);
            failures = pooled.WasHandedOut ? 0 : failures + 1;
            if (failures > 0 && GaveUp(slot, failures))
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
            var engine = await UciEngine.StartAsync(_engineOptions, _engineLogger, _stopping);
            LogStarted(_logger, _kind, slot, engine.ProcessId);
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
            LogStartFailed(_logger, e, _kind, slot, attempt, _startAttempts);
            return null;
        }
    }

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

        LogGaveUp(_logger, _kind, slot, failures);
        if (Interlocked.Decrement(ref _slotsTrying) == 0)
        {
            _idle.Writer.TryComplete();
        }

        return true;
    }

    private EngineUnavailableException Unavailable(string reason) => new(_kind, reason);

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
