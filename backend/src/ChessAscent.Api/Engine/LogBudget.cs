namespace ChessAscent.Api.Engine;

/// <summary>
/// Allows a fixed number of log entries per window of time, and counts the
/// ones it refuses, so that a stream with no end cannot flood the log.
/// </summary>
/// <remarks>Not thread-safe: each reader of an engine stream owns one.</remarks>
internal sealed class LogBudget(int entriesPerWindow, TimeSpan window)
{
    private long _windowStart = Environment.TickCount64;
    private int _taken;
    private int _suppressed;

    /// <summary>
    /// Whether one more entry may be logged now. When a new window has begun,
    /// <paramref name="suppressed"/> is how many entries the windows before it
    /// refused, so the caller can log that count first.
    /// </summary>
    public bool TryTake(out int suppressed)
    {
        suppressed = 0;
        var now = Environment.TickCount64;
        if (now - _windowStart >= (long)window.TotalMilliseconds)
        {
            suppressed = TakeSuppressed();
            _windowStart = now;
            _taken = 0;
        }

        if (_taken < entriesPerWindow)
        {
            _taken++;
            return true;
        }

        _suppressed++;
        return false;
    }

    /// <summary>The number of entries refused since this was last asked, which it then resets.</summary>
    public int TakeSuppressed()
    {
        var count = _suppressed;
        _suppressed = 0;
        return count;
    }
}
