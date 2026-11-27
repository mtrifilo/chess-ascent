namespace ChessAscent.Api.Engine;

// LEARN: A hosted service is code the host starts with the application and
// stops with it: the counterpart of a Spring bean that implements
// SmartLifecycle, or of @PostConstruct and @PreDestroy on a singleton. The
// host calls StartAsync on each registered hosted service, in registration
// order, before the application is reported started, and StopAsync in the
// reverse order when it shuts down (Ctrl+C, SIGTERM from `docker stop`, or
// the end of a test). Unlike SmartLifecycle, both methods are asynchronous,
// and StopAsync is handed a token that the host cancels when its shutdown
// timeout (HostOptions.ShutdownTimeout, 30 seconds unless set) runs out;
// after that the host stops waiting for it and goes on towards exit. BackgroundService
// is a base class for the common case of one long-running loop; this service
// has no loop of its own, so it implements the interface directly.
/// <summary>Starts the engine pool with the application and stops it, and every engine process, at shutdown.</summary>
internal sealed partial class EnginePoolService(EnginePool pool, ILogger<EnginePoolService> logger) : IHostedService
{
    // Returns at once: a missing or crashing engine must not hold up the
    // application, which serves without it and reports it through readiness.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        pool.Start();
        return Task.CompletedTask;
    }

    // EnginePoolOptionsValidator checks that each kind's engines stop within
    // the host's shutdown timeout, so the token should not run out. If it
    // does, the host goes on to dispose the DI container, and the pool's
    // DisposeAsync waits for the rest before the process exits.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopping(logger);
        try
        {
            await pool.StopAsync().WaitAsync(cancellationToken);
            LogStopped(logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStillStopping(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the engine pool")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine pool stopped; no engine process is left")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The engine pool had not stopped when the host's shutdown timeout ran out; it goes on stopping while the host is disposed")]
    private static partial void LogStillStopping(ILogger logger);
}
