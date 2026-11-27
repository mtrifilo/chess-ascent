namespace ChessAscent.Api.Engine;

// LEARN: A hosted service is code the host starts with the application and
// stops with it: the counterpart of a Spring bean that implements
// SmartLifecycle, or of @PostConstruct and @PreDestroy on a singleton. The
// host calls StartAsync on each registered hosted service, in registration
// order, before the application is reported started, and StopAsync in the
// reverse order when it shuts down (Ctrl+C, SIGTERM from `docker stop`, or
// the end of a test). Unlike SmartLifecycle, both methods are asynchronous,
// and StopAsync is handed a token that the host cancels when its shutdown
// timeout (HostOptions.ShutdownTimeout, 30 seconds unless set) runs out. The
// token is only a request: the host goes on awaiting StopAsync until it
// returns, so a service that must not hold up shutdown has to stop waiting
// by itself when the token is cancelled. BackgroundService
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

    // The token is not honoured: returning before the engines are gone
    // would let the application exit with processes still running. Stopping
    // is bounded anyway, by each kind's UciEngineOptions.LongestDisposal, and
    // EnginePoolOptionsValidator checks that this fits within the shutdown
    // timeout. The web server stops first, though, and spends the same
    // budget draining requests still in progress, so the token may already
    // be cancelled when this starts.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopping(logger);
        await pool.StopAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            LogStoppedLate(logger);
        }
        else
        {
            LogStopped(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the engine pool")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine pool stopped; no engine process is left")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine pool stopped, after the host's shutdown timeout had run out; no engine process is left")]
    private static partial void LogStoppedLate(ILogger logger);
}
