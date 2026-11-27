namespace ChessAscent.Api.Engine;

/// <summary>
/// The engines the pool runs, bound from the <c>EnginePool</c> section of
/// configuration. With no kinds configured the pool runs nothing.
/// </summary>
/// <remarks>
/// For example, in <c>appsettings.json</c>:
/// <code>
/// "EnginePool": {
///   "Kinds": {
///     "example": {
///       "ExecutablePath": "/path/to/engine",
///       "Arguments": [ "--flag" ],
///       "Processes": 2,
///       "HandshakeTimeout": "00:00:20",
///       ...
///     }
///   }
/// }
/// </code>
/// or, as environment variables, <c>EnginePool__Kinds__example__ExecutablePath</c>
/// and <c>EnginePool__Kinds__example__Arguments__0</c>.
/// </remarks>
// LEARN: An options class is the counterpart of a Spring Boot
// @ConfigurationProperties class: a plain class whose properties the
// configuration binder fills from one section of configuration, matching
// keys to property names without regard to case. EngineServices registers
// the binding. Two differences from Spring: the class is not itself a bean
// (code asks for IOptions<EnginePoolOptions> and reads its Value), and
// validation is a separate step, here a hand-written validator
// (EnginePoolOptionsValidator) rather than JSR-380 annotations.
public sealed class EnginePoolOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "EnginePool";

    /// <summary>
    /// Each kind of engine, by the name callers ask for it with. Names are
    /// compared without regard to case, as configuration keys are.
    /// </summary>
    public Dictionary<string, EngineKindOptions> Kinds { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One kind of engine: the program to run, how many processes of it, and how long to wait for them.</summary>
/// <remarks>
/// Only <see cref="Arguments"/>, <see cref="WorkingDirectory"/> and
/// <see cref="Processes"/> have defaults. Every wait and both restart
/// settings must be set: what is reasonable depends on the engine and on the
/// machine. A wait is written as <c>hh:mm:ss</c>, with a fraction if needed
/// (<c>00:00:00.500</c>); a bare number is read as days. Each must be greater
/// than zero and at most <see cref="int.MaxValue"/> milliseconds.
/// </remarks>
// LEARN: The properties are nullable (`TimeSpan?`, `int?`) so that the
// validator can tell a setting that is missing from one set to zero; a plain
// TimeSpan would bind to TimeSpan.Zero either way. `init` lets the binder,
// and an object initializer, set a property while the object is created,
// and nothing set it afterwards.
public sealed class EngineKindOptions
{
    /// <summary>The engine program.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Command-line arguments, passed to the program as given, without a shell.</summary>
    public IList<string> Arguments { get; } = [];

    /// <summary>The engine's working directory; the application's own when not set.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>How many processes of this kind run at once, each serving one caller at a time. At least 1; the default is 1.</summary>
    public int Processes { get; init; } = 1;

    /// <summary>The engine's handshake timeout (<see cref="UciEngineOptions.HandshakeTimeout"/>).</summary>
    public TimeSpan? HandshakeTimeout { get; init; }

    /// <summary>
    /// The engine's ready timeout (<see cref="UciEngineOptions.ReadyTimeout"/>),
    /// which also bounds the check the pool makes before it hands an engine out.
    /// </summary>
    public TimeSpan? ReadyTimeout { get; init; }

    /// <summary>The engine's search timeout (<see cref="UciEngineOptions.SearchTimeout"/>).</summary>
    public TimeSpan? SearchTimeout { get; init; }

    /// <summary>The engine's stop timeout (<see cref="UciEngineOptions.StopTimeout"/>).</summary>
    public TimeSpan? StopTimeout { get; init; }

    /// <summary>The engine's exit timeout (<see cref="UciEngineOptions.ExitTimeout"/>).</summary>
    public TimeSpan? ExitTimeout { get; init; }

    /// <summary>
    /// How long a caller waits for an engine of this kind, from asking until
    /// one is free and has passed its check. A check still running when the
    /// time is up goes on without the caller.
    /// </summary>
    public TimeSpan? CheckOutTimeout { get; init; }

    /// <summary>
    /// How many failures in a row a process slot may have before the pool
    /// stops trying to fill it, until the application restarts. A failure is
    /// a process that could not be started, or that ended or failed its
    /// check before it was first handed to a caller; one that was handed out
    /// resets the count. A process that ends during a conversation, or that
    /// a caller reports broken, is replaced (after
    /// <see cref="RestartDelay"/>) without counting, because its input may be
    /// the cause. At least 1.
    /// </summary>
    public int? StartAttempts { get; init; }

    /// <summary>The pause before each start of a slot's process after its first: after a failed start, and before a replacement.</summary>
    public TimeSpan? RestartDelay { get; init; }

    /// <summary>The client's options. Valid only once <see cref="EnginePoolOptionsValidator"/> has passed these.</summary>
    internal UciEngineOptions ToEngineOptions() => new()
    {
        ExecutablePath = ExecutablePath!,
        Arguments = [.. Arguments],
        WorkingDirectory = WorkingDirectory,
        HandshakeTimeout = HandshakeTimeout!.Value,
        ReadyTimeout = ReadyTimeout!.Value,
        SearchTimeout = SearchTimeout!.Value,
        StopTimeout = StopTimeout!.Value,
        ExitTimeout = ExitTimeout!.Value,
    };
}
