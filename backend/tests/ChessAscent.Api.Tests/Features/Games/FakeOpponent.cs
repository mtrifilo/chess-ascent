using System.Collections.Concurrent;
using ChessAscent.Api.Opponents;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>
/// An opponent whose answers the test writes, and which records every call
/// it gets, for the game endpoints' tests. The game flow is tested against
/// this rather than the fake engine: the opponent interface is the seam the
/// endpoints depend on, and the engine behind it has tests of its own.
/// </summary>
internal sealed class FakeOpponent : IOpponent
{
    private readonly ConcurrentQueue<OpponentCall> _calls = new();

    /// <summary>Gives the answer to each call. Unless the test sets it, every call is a test failure.</summary>
    public Func<OpponentCall, CancellationToken, Task<OpponentReply>> Answer { get; set; } =
        (call, _) => throw new InvalidOperationException($"The opponent was not expected to be asked, but was asked after {call}.");

    /// <summary>Every call so far, oldest first.</summary>
    public IReadOnlyCollection<OpponentCall> Calls => _calls;

    /// <summary>An opponent that answers its calls with these moves, one per call, in order.</summary>
    public static FakeOpponent Playing(params string[] moves)
    {
        var next = -1;
        return new FakeOpponent
        {
            Answer = (_, _) => Task.FromResult<OpponentReply>(new OpponentMove(moves[Interlocked.Increment(ref next)])),
        };
    }

    /// <summary>
    /// An opponent that gives no move the first time it is asked, with a
    /// reason holding <paramref name="secret"/>, which no response may show,
    /// and answers every later call with <paramref name="move"/>.
    /// </summary>
    public static FakeOpponent FailingOnceThenPlaying(string move, string secret)
    {
        var failures = 1;
        return new FakeOpponent
        {
            Answer = (_, _) => Task.FromResult<OpponentReply>(Interlocked.Decrement(ref failures) >= 0
                ? new OpponentFailed($"Engine said: {secret}")
                : new OpponentMove(move)),
        };
    }

    public async Task<OpponentReply> ChooseMoveAsync(IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken)
    {
        var call = new OpponentCall([.. moves], strength);
        _calls.Enqueue(call);
        return await Answer(call, cancellationToken);
    }
}

/// <summary>One call to <see cref="FakeOpponent"/>.</summary>
/// <param name="Moves">The moves it was given, as a copy.</param>
/// <param name="Strength">The strength it was given.</param>
internal sealed record OpponentCall(IReadOnlyList<string> Moves, int Strength)
{
    public override string ToString() => $"[{string.Join(' ', Moves)}] at {Strength}";
}
