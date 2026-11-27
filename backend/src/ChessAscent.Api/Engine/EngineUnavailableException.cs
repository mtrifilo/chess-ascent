namespace ChessAscent.Api.Engine;

/// <summary>
/// The pool could not give a caller an engine of the kind it asked for: none
/// was free and working within the check-out timeout, the kind has no
/// process left that the pool is still trying to start, or the pool is
/// stopping. The message says which.
/// </summary>
public sealed class EngineUnavailableException : Exception
{
    internal EngineUnavailableException(string kind, string reason)
        : base($"No engine of kind \"{kind}\" is available: {reason}.")
    {
        Kind = kind;
    }

    /// <summary>The kind of engine that was asked for.</summary>
    public string Kind { get; }
}
