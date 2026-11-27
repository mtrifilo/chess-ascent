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
    /// <summary>The longest wait accepted: <see cref="int.MaxValue"/> milliseconds.</summary>
    internal static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    /// The engine program. A bare name or a relative path is looked up with
    /// the application's own <c>PATH</c> and directories, not with
    /// <see cref="Environment"/>, so give a full path.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Command-line arguments, passed to the program as given, without a shell.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The engine's working directory; the application's own when null.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// The engine's environment variables, by name: the process starts with
    /// exactly these, and inherits none of the application's. An empty
    /// dictionary gives it an empty environment. Values are never logged.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>From start-up until the engine has answered <c>uci</c> with <c>uciok</c> and <c>isready</c> with <c>readyok</c>.</summary>
    public required TimeSpan HandshakeTimeout { get; init; }

    /// <summary>After an option is set, until the engine answers <c>isready</c> with <c>readyok</c>.</summary>
    public required TimeSpan ReadyTimeout { get; init; }

    /// <summary>
    /// For a whole search: from the <c>isready</c> that begins it until the
    /// engine answers with <c>bestmove</c>.
    /// </summary>
    public required TimeSpan SearchTimeout { get; init; }

    /// <summary>
    /// After a call gives up on a reply it is owed (because it was cancelled,
    /// timed out or the engine was disposed), until that reply comes: after
    /// <c>stop</c> for a search's <c>bestmove</c>, and likewise for
    /// <c>readyok</c>. Disposal also waits this long for a call in progress.
    /// </summary>
    public required TimeSpan StopTimeout { get; init; }

    /// <summary>
    /// Each wait around the process's end: after <c>quit</c>, until the
    /// process exits; after it is killed, until it is gone; after its output
    /// closes, until its exit code is known; after it exits, until its output
    /// closes; after a write finds its input closed, until it exits; and at
    /// disposal, after this side of the pipes is closed, until the readers
    /// finish.
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

        ArgumentNullException.ThrowIfNull(Environment);
        foreach (var (name, value) in Environment)
        {
            // The message names neither the variable's value nor, since it
            // may be malformed, the name itself.
            if (!IsValidEnvironmentName(name) || !IsValidEnvironmentValue(value))
            {
                throw new ArgumentException(
                    "An environment variable name must be non-empty with no '=' or NUL character, and its value must be non-null with no NUL character.",
                    nameof(Environment));
            }
        }

        ValidateWait(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateWait(ReadyTimeout, nameof(ReadyTimeout));
        ValidateWait(SearchTimeout, nameof(SearchTimeout));
        ValidateWait(StopTimeout, nameof(StopTimeout));
        ValidateWait(ExitTimeout, nameof(ExitTimeout));
    }

    /// <summary>
    /// Whether an environment variable name can be passed to a process: not
    /// empty, and with no <c>=</c> (which separates name from value) and no
    /// NUL character (which ends the string the operating system receives).
    /// </summary>
    internal static bool IsValidEnvironmentName(string? name) =>
        !string.IsNullOrEmpty(name) && !name.Contains('=', StringComparison.Ordinal) && !name.Contains('\0', StringComparison.Ordinal);

    /// <summary>Whether an environment variable value can be passed to a process: not null, with no NUL character. Empty is allowed.</summary>
    internal static bool IsValidEnvironmentValue(string? value) =>
        value is not null && !value.Contains('\0', StringComparison.Ordinal);

    /// <summary>Whether a wait is one this client accepts: greater than zero and at most <see cref="LongestWait"/>.</summary>
    internal static bool IsValidWait(TimeSpan wait) => wait > TimeSpan.Zero && wait <= LongestWait;

    /// <summary>
    /// The longest <see cref="UciEngine.DisposeAsync"/> can take with these
    /// waits: the stop timeout, for a call in progress, plus five exit
    /// timeouts, one for each wait around the process's end that disposal
    /// makes in turn.
    /// </summary>
    internal static TimeSpan LongestDisposal(TimeSpan stopTimeout, TimeSpan exitTimeout) => stopTimeout + (5 * exitTimeout);

    private static void ValidateWait(TimeSpan wait, string name)
    {
        if (!IsValidWait(wait))
        {
            throw new ArgumentOutOfRangeException(name, wait, $"A wait must be greater than zero and at most {LongestWait}.");
        }
    }
}
