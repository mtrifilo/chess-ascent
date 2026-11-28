using System.Collections.Concurrent;
using ChessAscent.Api.Hints;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>
/// A hint provider whose answers the test writes, and which records the moves
/// of every call it gets, for the hint endpoint's tests. As with
/// <see cref="FakeOpponent"/>, the endpoint is tested against the interface
/// it depends on, and the engine behind it has tests of its own.
/// </summary>
internal sealed class FakeHintProvider : IHintProvider
{
    private readonly ConcurrentQueue<IReadOnlyList<string>> _calls = new();

    /// <summary>Gives the answer to each call. Unless the test sets it, every call is a test failure.</summary>
    public Func<IReadOnlyList<string>, CancellationToken, Task<HintReply>> Answer { get; set; } =
        (moves, _) => throw new InvalidOperationException(
            $"No hint was expected to be asked for, but one was asked for after [{string.Join(' ', moves)}].");

    /// <summary>The moves of every call so far, each as a copy, oldest call first.</summary>
    public IReadOnlyCollection<IReadOnlyList<string>> Calls => _calls;

    /// <summary>A provider that answers every call with <paramref name="uci"/>.</summary>
    public static FakeHintProvider Hinting(string uci) =>
        new() { Answer = (_, _) => Task.FromResult<HintReply>(new HintMove(uci)) };

    public async Task<HintReply> FindBestMoveAsync(IReadOnlyList<string> moves, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> copy = [.. moves];
        _calls.Enqueue(copy);
        return await Answer(copy, cancellationToken);
    }
}
