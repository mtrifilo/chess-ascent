using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Engine;

/// <summary>
/// Long-lived engine processes, a fixed number of each kind that
/// configuration describes, shared between requests: a caller borrows one
/// for a conversation and the pool takes it back.
/// </summary>
/// <remarks>
/// <para>
/// One process serves one caller at a time. Engines are treated as holding
/// no state between conversations: send the whole position each time, and
/// set any option a search depends on, because the previous caller may have
/// changed it.
/// </para>
/// <para>
/// The processes start in the background once <see cref="EnginePoolService"/>
/// starts the pool, so the application serves before they are ready; the
/// readiness check reports each kind until it has a working process. A
/// process that ends, fails the check before hand-out, or is reported broken
/// is stopped and replaced after <see cref="EngineKindOptions.RestartDelay"/>.
/// A slot whose process fails <see cref="EngineKindOptions.StartAttempts"/>
/// times in a row (see there for what counts) stays empty until the
/// application restarts.
/// </para>
/// <para>
/// It knows no particular engine: the kinds, their programs and their
/// arguments all come from <see cref="EnginePoolOptions"/>.
/// </para>
/// </remarks>
public sealed class EnginePool : IAsyncDisposable
{
    private readonly Dictionary<string, EngineKindPool> _kinds;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _lifecycle = new();
    private Task? _running;
    private Task? _stop;

    public EnginePool(IOptions<EnginePoolOptions> options, ILogger<EnginePool> logger, ILogger<UciEngine> engineLogger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _kinds = options.Value.Kinds.ToDictionary(
            entry => entry.Key,
            entry => new EngineKindPool(entry.Key, entry.Value, logger, engineLogger, _stopping.Token),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks out an engine of the kind, runs the conversation with it, and
    /// gives the engine back, however the conversation ends: when it returns,
    /// throws, or is cancelled.
    /// </summary>
    /// <remarks>
    /// Waiting for a free engine is bounded by the kind's
    /// <see cref="EngineKindOptions.CheckOutTimeout"/>, and includes an
    /// <c>isready</c> check of the engine before it is handed over; the
    /// caller's token ends the wait at once, even during the check. The
    /// conversation itself is not bounded here: each call it makes on the
    /// engine is bounded by the engine's own timeouts. An engine that is no
    /// longer usable when the conversation ends (see
    /// <see cref="UciEngine.IsUsable"/>), or that the conversation reported
    /// broken, is replaced rather than given to the next caller.
    /// </remarks>
    /// <param name="kind">The kind of engine, as named in configuration.</param>
    /// <param name="conversation">Uses the engine. Pass it <paramref name="cancellationToken"/> or a token of its own.</param>
    /// <param name="cancellationToken">Ends the wait for an engine.</param>
    /// <exception cref="ArgumentException">No kind of that name is configured.</exception>
    /// <exception cref="EngineUnavailableException">No engine of the kind could be had; the message says why.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while waiting for an engine.</exception>
    public async Task<T> RunAsync<T>(string kind, Func<EngineLease, Task<T>> conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(conversation);
        if (!_kinds.TryGetValue(kind, out var pool))
        {
            throw new ArgumentException($"No engine kind named \"{kind}\" is configured.", nameof(kind));
        }

        var pooled = await pool.CheckOutAsync(cancellationToken);
        var lease = new EngineLease(pooled.Engine);
        try
        {
            return await conversation(lease);
        }
        finally
        {
            lease.End();
            pool.Return(pooled, lease.IsReportedBroken);
        }
    }

    /// <summary>The configured kinds that have no working process at the moment, idle or busy.</summary>
    internal IReadOnlyList<string> KindsWithoutWorkingEngine() =>
        [.. _kinds.Values.Where(pool => !pool.HasWorkingEngine).Select(pool => pool.Kind)];

    /// <summary>Starts every slot's process in the background, and returns at once. Called by <see cref="EnginePoolService"/>.</summary>
    /// <exception cref="ObjectDisposedException">The pool has been stopped.</exception>
    internal void Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_stop is not null, this);
            _running ??= Task.WhenAll(_kinds.Values.Select(pool => pool.RunAsync()));
        }
    }

    /// <summary>
    /// Stops every process and returns once all are gone: quit, then a kill
    /// if the engine has not exited in time (see <see cref="UciEngine.DisposeAsync"/>).
    /// Callers waiting for an engine get <see cref="EngineUnavailableException"/>,
    /// and conversations in progress find their engine disposed. Safe to call
    /// more than once.
    /// </summary>
    internal Task StopAsync()
    {
        lock (_lifecycle)
        {
            return _stop ??= StopOnceAsync();
        }
    }

    /// <summary>Stops the pool, as <see cref="StopAsync"/> does; the DI container calls it when it is disposed.</summary>
    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopOnceAsync()
    {
        // Every wait for an engine is linked to this token, so cancelling it
        // ends them all, and each slot then stops its process.
        await _stopping.CancelAsync();

        // Start() cannot set this once _stop is set, so it is read without the lock.
        if (_running is { } running)
        {
            await running;
        }
    }
}
