namespace ChessAscent.Api.Engine;

/// <summary>How to start one engine process, and how long to wait for each of its replies.</summary>
/// <remarks>
/// The waits have no defaults: what is reasonable depends on the engine and
/// on the machine, so whoever configures an engine chooses them. Each must be
/// greater than zero and at most <see cref="int.MaxValue"/> milliseconds
/// (about 24.8 days): a longer wait is taken as a configuration mistake.
/// </remarks>
// LEARN: `required` makes the compiler insist that every object initializer
// sets the property (`new UciEngineOptions { ExecutablePath = ..., ... }`),
// so there is no half-built instance and no builder class to write. `init`
// means the property can be set only while the object is being created,
// which makes the record immutable afterwards, like a Java record's
// components. A `record` (here a class, as Java's are) also gets `with`,
// which copies an instance with some properties changed:
// `options with { SearchTimeout = TimeSpan.FromSeconds(5) }`.
public sealed record UciEngineOptions
{
    /// <summary>The engine program.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Command-line arguments, passed to the program as given, without a shell.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The engine's working directory; the application's own when null.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>From start-up until the engine has answered <c>uci</c> with <c>uciok</c> and <c>isready</c> with <c>readyok</c>.</summary>
    public required TimeSpan HandshakeTimeout { get; init; }

    /// <summary>After an option is set, until the engine answers <c>isready</c> with <c>readyok</c>.</summary>
    public required TimeSpan ReadyTimeout { get; init; }

    /// <summary>
    /// For a whole search: from the <c>isready</c> that begins it until the
    /// engine answers with <c>bestmove</c>.
    /// </summary>
    public required TimeSpan SearchTimeout { get; init; }

    /// <summary>After <c>stop</c> ends a search early, until the engine answers with <c>bestmove</c>.</summary>
    public required TimeSpan StopTimeout { get; init; }

    /// <summary>
    /// Each wait around the process's end: after <c>quit</c>, until the
    /// process exits; after it is killed, until it is gone; after its output
    /// closes, until its exit code is known; after it exits, until its output
    /// closes; and after a write finds its input closed, until it exits.
    /// </summary>
    public required TimeSpan ExitTimeout { get; init; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        ArgumentNullException.ThrowIfNull(Arguments);
        foreach (var argument in Arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(Arguments));
        }

        ValidateWait(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateWait(ReadyTimeout, nameof(ReadyTimeout));
        ValidateWait(SearchTimeout, nameof(SearchTimeout));
        ValidateWait(StopTimeout, nameof(StopTimeout));
        ValidateWait(ExitTimeout, nameof(ExitTimeout));
    }

    private static void ValidateWait(TimeSpan wait, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wait, TimeSpan.Zero, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wait, TimeSpan.FromMilliseconds(int.MaxValue), name);
    }
}
