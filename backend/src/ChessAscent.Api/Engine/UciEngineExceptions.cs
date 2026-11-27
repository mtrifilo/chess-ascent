namespace ChessAscent.Api.Engine;

/// <summary>An engine failed. Each subclass says how, so that whoever owns the engine can decide what to do.</summary>
public abstract class UciEngineException : Exception
{
    // LEARN: `private protected` means "subclasses in this assembly only",
    // which Java has no modifier for: code in another project can catch these
    // exceptions but cannot derive new kinds from them.
    private protected UciEngineException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The engine process ended, or closed its input or output, while the client
/// still needed it. The engine is unusable and must be replaced.
/// </summary>
public sealed class UciEngineExitedException : UciEngineException
{
    private UciEngineExitedException(int? exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// The exit code, or null if the process had not exited (it closed a pipe
    /// instead) or its exit code could not be read in time. On Linux and macOS
    /// a process ended by a signal reports 128 plus the signal number: 137 for
    /// SIGKILL, 139 for a segmentation fault.
    /// </summary>
    public int? ExitCode { get; }

    internal static UciEngineExitedException Exited(int? exitCode) =>
        exitCode is { } code ? new(code, $"The engine process exited with code {code}.") : ClosedOutput();

    internal static UciEngineExitedException ClosedOutput() => new(null, "The engine process closed its output.");

    internal static UciEngineExitedException ClosedInput() => new(null, "The engine process closed its input.");
}

/// <summary>
/// The engine did not give an expected reply in time. Whether it can still
/// be used depends on whether the conversation could be brought back into
/// step: see <see cref="UciEngine.IsUsable"/>.
/// </summary>
public sealed class UciEngineTimeoutException : UciEngineException
{
    internal UciEngineTimeoutException(string? awaited, TimeSpan timeout, bool commandCutOff)
        : base(Describe(awaited, timeout, commandCutOff))
    {
        Awaited = awaited;
        Timeout = timeout;
    }

    /// <summary>
    /// The reply the client was waiting for: <c>uciok</c>, <c>readyok</c> or
    /// <c>bestmove</c>. Null when the time ran out while no reply was awaited,
    /// which has two causes: a command was being sent and the engine was not
    /// taking it (it has stopped reading its input), or the time ran out
    /// between two commands of the call, before the next was written. In the
    /// first case the engine is unusable; in the second nothing was lost and it
    /// stays usable. <see cref="UciEngine.IsUsable"/> tells them apart.
    /// </summary>
    public string? Awaited { get; }

    /// <summary>The time allowed for the whole call, from the timeout that applies to it.</summary>
    public TimeSpan Timeout { get; }

    private static string Describe(string? awaited, TimeSpan timeout, bool commandCutOff)
    {
        var milliseconds = timeout.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return awaited is not null
            ? $"The engine did not answer with {awaited} within {milliseconds} ms."
            : commandCutOff
                ? $"The engine did not take a command within {milliseconds} ms; it has stopped reading its input."
                : $"The call ran out of its {milliseconds} ms between two commands, before the next was sent.";
    }
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
