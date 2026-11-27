namespace ChessAscent.Api.Engine;

/// <summary>
/// An engine lent to one caller for one conversation by
/// <see cref="EnginePool.RunAsync{T}"/>. It is valid only until the
/// conversation returns; the pool then takes the engine back.
/// </summary>
public sealed class EngineLease
{
    private readonly UciEngine _engine;
    private volatile bool _ended;
    private volatile bool _reportedBroken;

    internal EngineLease(UciEngine engine) => _engine = engine;

    /// <summary>
    /// The engine. Do not keep it beyond the conversation: once the
    /// conversation returns, another caller may have it, and this property
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public UciEngine Engine => _ended
        ? throw new InvalidOperationException("The conversation has ended, and the engine has gone back to the pool.")
        : _engine;

    internal bool IsReportedBroken => _reportedBroken;

    /// <summary>
    /// Says the engine misbehaved in a way the client cannot see, for example
    /// by answering with a move that is not legal in the position it was
    /// sent. The pool then stops it and starts another, rather than handing
    /// it to the next caller. Failures the client does see (the process
    /// exiting, a reply that never came) need no report.
    /// </summary>
    public void ReportBroken() => _reportedBroken = true;

    internal void End() => _ended = true;
}
