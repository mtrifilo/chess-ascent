using System.Diagnostics;

namespace ChessAscent.Chess.Tests;

// The six standard perft positions and their published counts, from
// https://www.chessprogramming.org/Perft_Results. Between them they reach
// castling on both sides, en passant, promotion and under-promotion, discovered
// and double check, and pins. Every test here goes through IChessRules and
// IChessGame only, so the same counts are the acceptance gate for any engine
// placed behind the interface.
public sealed class PerftTests(ITestOutputHelper output)
{
    // LEARN: xUnit has two kinds of test. A [Fact] takes no arguments, like a
    // plain JUnit @Test. A [Theory] takes arguments and runs once per row of
    // data, like JUnit 5's @ParameterizedTest; each row is reported as its own
    // test with its arguments in the name. [InlineData] (seen in other files)
    // is @CsvSource with typed values, and [MemberData] names a static member
    // that returns the rows, like @MethodSource. TheoryData<T1, T2> is a typed
    // list of rows, so a row with the wrong argument types does not compile.
    // MemberData may pass arguments to that member, as here.
    [Theory]
    [MemberData(nameof(PositionsUpToDepth), 4)]
    public void CountMatchesThePublishedCount(string position, int depth) => AssertPublishedCount(position, depth);

    // LEARN: Two attributes make this the slow tier. [Trait] attaches a
    // name/value label, like JUnit 5's @Tag("slow"); runners filter on it
    // (`--filter-trait "Category=Slow"`). Explicit = true goes further than a
    // tag: the test is left out of every run unless the runner is asked for
    // explicit tests (`--explicit only` or `--explicit on`). JUnit gets the
    // same effect from a @Tag plus an excludedGroups setting in the build
    // file; here the test itself says it is opt-in, so no build setting can
    // forget to exclude it. The command is in CLAUDE.md under "Back end".
    [Theory(Explicit = true)]
    [Trait("Category", "Slow")]
    [MemberData(nameof(PositionsAtDepth), 5)]
    public void DepthFiveCountMatchesThePublishedCount(string position, int depth) => AssertPublishedCount(position, depth);

    public static TheoryData<string, int> PositionsUpToDepth(int maxDepth)
    {
        var rows = new TheoryData<string, int>();
        foreach (var position in PerftPosition.All)
        {
            for (var depth = 1; depth <= maxDepth; depth++)
            {
                rows.Add(position.Name, depth);
            }
        }

        return rows;
    }

    public static TheoryData<string, int> PositionsAtDepth(int depth) =>
        [.. PerftPosition.All.Select(position => (position.Name, depth))];

    private void AssertPublishedCount(string name, int depth)
    {
        var position = PerftPosition.Named(name);
        var expected = position.Counts[depth - 1];

        var clock = Stopwatch.StartNew();
        var divide = Perft.Divide(Games.Load(position.Fen), depth);
        var actual = divide.Sum(line => line.Count);
        clock.Stop();

        // LEARN: ITestOutputHelper is xUnit's per-test output, injected through
        // the primary constructor above. Text written to it is attached to this
        // test's result (shown for every test with `--output Detailed`, and for
        // a failure always), rather than interleaved on the console as
        // System.out is when JUnit runs tests in parallel.
        output.WriteLine($"{name} depth {depth}: expected {expected}, actual {actual}, {clock.Elapsed.TotalSeconds:F2} s");

        if (actual != expected)
        {
            var divideText = Perft.Format(divide);
            Assert.Fail(
                $"Perft of {name} at depth {depth} counted {actual} positions; the published count is {expected}.\n"
                + $"FEN: {position.Fen}\n"
                + $"Count per first move, in the layout of Stockfish's `position fen <FEN>` then `go perft {depth}`;\n"
                + "diff the two to find the move whose count differs, then repeat one level down from the position after it:\n"
                + divideText);
        }
    }
}

/// <summary>A standard perft position and its published counts, from depth 1 upwards.</summary>
internal sealed record PerftPosition(string Name, string Fen, long[] Counts)
{
    // Kiwipete is published without move counters; the interface takes
    // six-field FEN, so "0 1" is added, which changes no count.
    public static readonly PerftPosition[] All =
    [
        new("Initial", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", [20, 400, 8_902, 197_281, 4_865_609]),
        new("Kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", [48, 2_039, 97_862, 4_085_603, 193_690_690]),
        new("Position3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", [14, 191, 2_812, 43_238, 674_624]),
        new("Position4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", [6, 264, 9_467, 422_333, 15_833_292]),
        new("Position5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", [44, 1_486, 62_379, 2_103_487, 89_941_194]),
        new("Position6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", [46, 2_079, 89_890, 3_894_594, 164_075_551]),
    ];

    public static PerftPosition Named(string name) => All.Single(position => position.Name == name);
}
