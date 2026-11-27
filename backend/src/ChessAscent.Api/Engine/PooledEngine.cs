namespace ChessAscent.Api.Engine;

/// <summary>
/// One engine process in the pool, and whether it is idle, checked out, or
/// retired. Each state change happens under a lock, so that an engine is
/// never handed to two callers, and one retired while idle is never handed
/// out again.
/// </summary>
/// <remarks>
/// It starts idle, because the pool puts it straight into the idle queue.
/// The queue may still hold an entry for it after it is retired;
/// <see cref="TryCheckOut"/> refuses that entry.
/// </remarks>
internal sealed class PooledEngine(UciEngine engine)
{
    private readonly Lock _lock = new();

    // LEARN: A TaskCompletionSource is a Task that code completes by hand,
    // the counterpart of a CompletableFuture finished with complete(). With
    // RunContinuationsAsynchronously, whatever awaits the task resumes on the
    // thread pool rather than inside the TrySetResult call, so a caller that
    // retires an engine does not go on to run the pool's replacement work on
    // its own thread.
    private readonly TaskCompletionSource<string> _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private State _state = State.Idle;
    private volatile bool _handedOut;

    private enum State
    {
        Idle,
        CheckedOut,
        Retired,
    }

    public UciEngine Engine => engine;

    /// <summary>Completes with the reason once the engine is retired.</summary>
    public Task<string> Retired => _retired.Task;

    /// <summary>Whether the engine has ever passed its check and been handed to a caller.</summary>
    public bool WasHandedOut => _handedOut;

    /// <summary>Takes the engine for a caller, if it is idle.</summary>
    public bool TryCheckOut() => TryMove(State.Idle, State.CheckedOut);

    /// <summary>Records that the engine passed its check and went to a caller.</summary>
    public void MarkHandedOut() => _handedOut = true;

    /// <summary>Makes a checked-out engine idle again; false if it was retired meanwhile.</summary>
    public bool TryReturn() => TryMove(State.CheckedOut, State.Idle);

    /// <summary>Retires the engine if nobody has it. A caller that has it returns it, and the return retires it.</summary>
    public void RetireIfIdle(string reason)
    {
        if (TryMove(State.Idle, State.Retired))
        {
            _retired.TrySetResult(reason);
        }
    }

    /// <summary>Retires the engine, whatever its state. The first reason given is kept.</summary>
    public void Retire(string reason)
    {
        lock (_lock)
        {
            _state = State.Retired;
        }

        _retired.TrySetResult(reason);
    }

    private bool TryMove(State from, State to)
    {
        lock (_lock)
        {
            if (_state != from)
            {
                return false;
            }

            _state = to;
            return true;
        }
    }
}
