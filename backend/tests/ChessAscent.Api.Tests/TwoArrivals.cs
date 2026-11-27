namespace ChessAscent.Api.Tests;

/// <summary>
/// Holds the first of two callers until the second arrives, so a race test
/// can put two requests past the same point before either goes on.
/// </summary>
/// <remarks>
/// A caller that waits more than ten seconds goes ahead, so a request that
/// never arrives fails the test instead of hanging it. Callers after the
/// second pass straight through.
/// </remarks>
internal sealed class TwoArrivals
{
    private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public bool BothArrived => _bothArrived.Task.IsCompletedSuccessfully;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        var arrival = Interlocked.Increment(ref _arrived);
        if (arrival == 2)
        {
            _bothArrived.TrySetResult();
        }
        else if (arrival == 1)
        {
            await Task.WhenAny(_bothArrived.Task, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
        }
    }
}
