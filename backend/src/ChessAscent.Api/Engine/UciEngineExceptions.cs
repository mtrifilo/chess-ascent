namespace ChessAscent.Api.Engine;

/// <summary>An engine failed. Each subclass says how, so that whoever owns the engine can decide what to do.</summary>
public abstract class UciEngineException : Exception
{
    private protected UciEngineException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The engine process ended, or closed its output, while the client still
/// needed it. The engine is unusable and must be replaced.
/// </summary>
public sealed class UciEngineExitedException : UciEngineException
{
    internal UciEngineExitedException(int? exitCode)
        : base(exitCode is { } code ? $"The engine process exited with code {code}." : "The engine process closed its output.")
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// The exit code, or null if the process had not exited within the exit
    /// timeout after closing its output. On Linux and macOS a process ended by
    /// a signal reports 128 plus the signal number: 139 for a segmentation fault.
    /// </summary>
    public int? ExitCode { get; }
}

/// <summary>
/// The engine did not give an expected reply in time. Whether it can still
/// be used depends on whether the conversation could be brought back into
/// step: see <see cref="UciEngine.IsUsable"/>.
/// </summary>
public sealed class UciEngineTimeoutException : UciEngineException
{
    internal UciEngineTimeoutException(string awaited, TimeSpan timeout)
        : base($"The engine did not answer with {awaited} within {timeout.TotalMilliseconds:0} ms.")
    {
        Awaited = awaited;
        Timeout = timeout;
    }

    /// <summary>The reply the client was waiting for, such as <c>bestmove</c>.</summary>
    public string Awaited { get; }

    /// <summary>How long the client waited.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The engine gave the awaited reply in a form this client cannot read, such
/// as <c>bestmove</c> with no move. The conversation is still in step, so the
/// engine can still be used.
/// </summary>
public sealed class UciProtocolException : UciEngineException
{
    internal UciProtocolException(string message)
        : base(message)
    {
    }
}
