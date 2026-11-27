namespace ChessAscent.FakeUciEngine;

/// <summary>
/// One search, running while the command loop goes on reading, so that a
/// "stop" can end it early as it would a real engine's.
/// </summary>
internal sealed class Search
{
    // What a search ended early by "stop" answers: a legal move, but not a
    // searched one, as real engines do.
    private const string StoppedBestMove = "a7a6";

    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _running;

    public Search(Settings settings, int milliseconds) => _running = RunAsync(settings, milliseconds);

    public async Task StopAsync()
    {
        _stop.TrySetResult();
        await _running;
    }

    private async Task RunAsync(Settings settings, int milliseconds)
    {
        // Let the command loop return to reading before the search writes.
        await Task.Yield();

        Console.Out.WriteLine("info string fake search started");
        Console.Out.WriteLine("info string bestmove a1a1");
        foreach (var length in settings.LongLines)
        {
            Console.Out.WriteLine("info " + new string('x', length));
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

        Console.Out.WriteLine("info depth 1 score cp 13 pv e2e4");
        var answer = "bestmove " + (stopped ? StoppedBestMove : settings.BestMove);

        // One write, so that nothing the command loop prints comes between the
        // two lines, as in a single-threaded engine.
        Console.Out.Write(settings.ExtraBestMove is { } extra
            ? answer + Console.Out.NewLine + "bestmove " + extra + Console.Out.NewLine
            : answer + Console.Out.NewLine);
    }
}
