namespace ChessAscent.FakeUciEngine;

/// <summary>
/// One search, running while the command loop goes on reading, so that a
/// "stop" can end it early as it would a real engine's.
/// </summary>
internal sealed class Search
{
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _running = Task.CompletedTask;

    private Search()
    {
    }

    public static Search Start(Output output, Settings settings, int milliseconds)
    {
        var search = new Search();
        search._running = search.RunAsync(output, settings, milliseconds);
        return search;
    }

    public async Task StopAsync()
    {
        _stop.TrySetResult();
        await _running;
    }

    private async Task RunAsync(Output output, Settings settings, int milliseconds)
    {
        // Let the command loop return to reading before the search writes.
        await Task.Yield();

        output.Send("info string fake search started");
        output.Send("info string bestmove a1a1");
        foreach (var length in settings.LongLines)
        {
            output.Send("info " + new string('x', length));
        }

        var stopped = true;
        try
        {
            await _stop.Task.WaitAsync(TimeSpan.FromMilliseconds(milliseconds));
        }
        catch (TimeoutException)
        {
            stopped = false;
        }

        output.Send("info depth 1 score cp 13 pv e2e4");
        output.Send("bestmove " + (stopped ? settings.StoppedBestMove : settings.BestMove));
        if (settings.ExtraBestMove is { } extra)
        {
            output.Send("bestmove " + extra);
        }
    }
}
