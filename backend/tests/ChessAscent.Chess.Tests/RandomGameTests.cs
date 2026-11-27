namespace ChessAscent.Chess.Tests;

// Perft starts from fixed positions, so it cannot reach rules that depend on
// history: castling rights lost on the way, repetition, the fifty-move count.
// Long random games do. Each test below checks one invariant at every
// position of every game, and names the seed and the moves on failure.
//
// LEARN: IClassFixture<T> marks a fixture: xUnit creates one T before the
// first test in this class runs, passes it to the constructor of each test
// instance, and disposes it after the last if it is IDisposable. As in JUnit,
// every test gets a fresh instance of the test class, so the fixture is how
// tests share something costly. JUnit 5 would hold it in a static field set
// by @BeforeAll; here it is an ordinary object handed in, so there is no
// static state, and tests in different classes cannot see each other's.
public sealed class RandomGameTests(RandomGames games) : IClassFixture<RandomGames>
{
    // Checking every other UCI move (20,160 candidates) costs up to about
    // 40 ms per position, against well under a millisecond for the other
    // checks, so it runs on every tenth position and on the last one of each
    // game. The seed shifts which positions those are, so the games between
    // them cover every stage of a game.
    private const int RefusalCheckInterval = 10;

    public static TheoryData<int> Seeds() => [.. RandomGames.Seeds];

    // The invariants below are only as strong as the games they run over.
    // This fails if a change to the seeds, the length limit or the engine
    // leaves the games without some way of ending, or without castling or
    // promotion, so that a check would quietly cover less.
    [Fact]
    public void TheGamesReachEveryWayAGameCanEndAndTheSpecialMoves()
    {
        var all = RandomGames.Seeds.Select(seed => games[seed]).ToList();

        all.Select(game => game.Status.Value).Should().Contain(
        [
            new Checkmate(Side.White),
            new Checkmate(Side.Black),
            new Stalemate(),
            new Draw(DrawReason.ThreefoldRepetition),
            new Draw(DrawReason.FiftyMoveRule),
            new Draw(DrawReason.DeadPosition),
        ]);

        var sans = all.SelectMany(game => game.History).Select(move => move.San).ToList();
        sans.Should().Contain(san => san.StartsWith("O-O", StringComparison.Ordinal), "some game castles");
        sans.Should().Contain(san => san.Contains('=', StringComparison.Ordinal), "some game promotes");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryPositionHasLegalMovesUnlessTheGameIsOver(int seed)
    {
        var recorded = games[seed];
        foreach (var (ply, game) in RandomGames.Positions(recorded))
        {
            var moves = game.LegalMoves();
            var context = recorded.At(ply);

            (game.Status.Value is Ongoing).Should().Be(moves.Count > 0, "the status is {0} in {1}", game.Status, context);
            moves.Select(move => move.Uci).Should().BeInAscendingOrder(StringComparer.Ordinal, "in {0}", context)
                .And.OnlyHaveUniqueItems("in {0}", context);
        }

        (recorded.Status.Value is not Ongoing || recorded.Moves.Count == RandomGames.MaxPlies)
            .Should().BeTrue("a game stops only when it is over or reaches the length limit");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ReplayingTheMovesReproducesTheGame(int seed)
    {
        var recorded = games[seed];

        var replayed = Games.Play([.. recorded.Moves]);

        replayed.Fen.Should().Be(recorded.Fen, "in {0}", recorded.At(recorded.Moves.Count));
        replayed.Status.Value.Should().Be(recorded.Status.Value);
        replayed.History.Should().Equal(recorded.History);
    }

    // A FEN holds no history, so the reloaded game cannot know about earlier
    // occurrences of the position: a game drawn by repetition reloads as
    // ongoing. Everything else a FEN describes must survive.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryPositionSurvivesAFenRoundTrip(int seed)
    {
        var recorded = games[seed];
        foreach (var (ply, game) in RandomGames.Positions(recorded))
        {
            var context = recorded.At(ply);
            var fen = game.Fen;
            var reloaded = Games.Load(fen);

            reloaded.Fen.Should().Be(fen, "in {0}", context);
            if (game.Status.Value is not Draw { Reason: DrawReason.ThreefoldRepetition })
            {
                reloaded.Status.Value.Should().Be(game.Status.Value, "in {0}", context);
                reloaded.LegalMoves().Should().Equal(game.LegalMoves(), "in {0}", context);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryListedMoveIsPlayedAndEveryOtherMoveIsRefused(int seed)
    {
        var recorded = games[seed];
        foreach (var (ply, game) in RandomGames.Positions(recorded))
        {
            var context = recorded.At(ply);
            var legal = game.LegalMoves();

            AssertEveryListedMoveIsPlayedAsListed(game, legal, context);

            var isLast = ply == recorded.Moves.Count;
            if ((ply + seed) % RefusalCheckInterval == 0 || isLast)
            {
                // On a branch, so that a candidate wrongly accepted cannot
                // move the replayed game on and spoil every later check.
                AssertEveryOtherMoveIsRefused(game.Branch(), legal, context);
            }
        }
    }

    // These two helpers compare plainly, collect the mismatches and make one
    // assertion per position, because an assertion per move would cost more
    // than playing the move.
    private static void AssertEveryListedMoveIsPlayedAsListed(IChessGame game, IReadOnlyList<LegalMove> legal, GamePosition context)
    {
        var historyLength = game.History.Count;
        var mismatches = new List<string>();
        foreach (var move in legal)
        {
            var branch = game.Branch();
            var result = branch.TryPlay(move.Uci);
            if (result is not MovePlayed played)
            {
                mismatches.Add($"{move.Uci} is listed but gave {result}");
                continue;
            }

            if (played.Move != new PlayedMove(move.Uci, move.San))
            {
                mismatches.Add($"{move.Uci} is listed as {move.San} but was recorded as {played.Move}");
            }

            if (!Equals(played.Status.Value, branch.Status.Value))
            {
                mismatches.Add($"{move.Uci} reported {played.Status} but left the game {branch.Status}");
            }

            var history = branch.History;
            if (history.Count != historyLength + 1 || history[^1] != played.Move)
            {
                mismatches.Add($"{move.Uci} is not the last of {history.Count} moves in the history");
            }
        }

        mismatches.Should().BeEmpty("every listed move is played as listed in {0}", context);
    }

    private static void AssertEveryOtherMoveIsRefused(IChessGame game, IReadOnlyList<LegalMove> legal, GamePosition context)
    {
        var fen = game.Fen;
        var historyLength = game.History.Count;
        var listed = legal.Select(move => move.Uci).ToHashSet(StringComparer.Ordinal);
        var status = game.Status;

        var wronglyHandled = new List<string>();
        foreach (var candidate in RandomGames.EveryUciMove)
        {
            if (listed.Contains(candidate))
            {
                continue;
            }

            PlayResult expected = status.Value is Ongoing ? new IllegalMove(candidate) : new GameAlreadyOver(status);
            var actual = game.TryPlay(candidate);
            if (!Equals(actual.Value, expected.Value))
            {
                wronglyHandled.Add($"{candidate} gave {actual}");
            }
        }

        wronglyHandled.Should().BeEmpty("only listed moves may be played in {0}", context);
        game.Fen.Should().Be(fen, "a refused move leaves the game unchanged");
        game.History.Should().HaveCount(historyLength);
    }
}

/// <summary>A game played by choosing uniformly among the legal moves, and how it ended.</summary>
public sealed record RandomGame(int Seed, IReadOnlyList<string> Moves, string Fen, GameStatus Status, IReadOnlyList<PlayedMove> History)
{
    public GamePosition At(int ply) => new(this, ply);
}

/// <summary>
/// A position in a recorded game, named well enough to rebuild it: the seed
/// and the moves that lead to it.
/// </summary>
/// <remarks>
/// Assertions format their reasons only when they fail, so passing this
/// rather than its text keeps the move list from being joined at every
/// position of every game.
/// </remarks>
public readonly record struct GamePosition(RandomGame Game, int Ply)
{
    public override string ToString() =>
        $"seed {Game.Seed} after {Ply} half-moves from the starting position: {string.Join(' ', Game.Moves.Take(Ply))}";
}

/// <summary>The seeded random games, played once for every test in <see cref="RandomGameTests"/>.</summary>
public sealed class RandomGames
{
    public const int MaxPlies = 400;

    // Fixed seeds, so a failure reproduces. A seed added here is a new game.
    public static readonly int[] Seeds = [.. Enumerable.Range(1, 40)];

    /// <summary>Every well-formed UCI move: two different squares, then optionally a promotion letter.</summary>
    public static readonly string[] EveryUciMove = BuildEveryUciMove();

    private readonly Dictionary<int, RandomGame> bySeed = Seeds.ToDictionary(seed => seed, Play);

    public RandomGame this[int seed] => bySeed[seed];

    // LEARN: A method that uses `yield return` is an iterator: the compiler
    // turns its body into an IEnumerable whose elements are produced one at a
    // time as the caller's foreach asks for them, picking up where it left
    // off. Java has no equivalent and would need a hand-written Iterator
    // class. Here it lets each test walk a game position by position without
    // knowing how the game is replayed. The game it yields is the same object
    // each time, one move further on, so callers play moves only on a Branch of it.
    /// <summary>Replays a recorded game, yielding the game at each position from the start to the end.</summary>
    public static IEnumerable<(int Ply, IChessGame Game)> Positions(RandomGame recorded)
    {
        var game = Games.Rules.NewGame();
        yield return (0, game);
        for (var index = 0; index < recorded.Moves.Count; index++)
        {
            Games.PlayOn(game, recorded.Moves[index]);
            yield return (index + 1, game);
        }
    }

    // LEARN: new Random(seed) gives the same sequence every time on one .NET
    // version, which is what makes these games repeatable. Unlike Java's
    // java.util.Random, whose algorithm is fixed by its specification, .NET
    // documents that a seed may give a different sequence on another .NET
    // version. That is why a failure prints the moves as well as the seed:
    // the moves rebuild the position on any runtime. The choice depends only
    // on the number of legal moves and their order, which IChessGame fixes
    // (ordered by UCI text), so another engine behind the interface plays the
    // same games.
    private static RandomGame Play(int seed)
    {
        var random = new Random(seed);
        var game = Games.Rules.NewGame();
        var moves = new List<string>();

        while (moves.Count < MaxPlies && game.Status.Value is Ongoing)
        {
            var legal = game.LegalMoves();
            var choice = legal[random.Next(legal.Count)].Uci;
            Games.PlayOn(game, choice);
            moves.Add(choice);
        }

        return new RandomGame(seed, moves, game.Fen, game.Status, game.History);
    }

    private static string[] BuildEveryUciMove()
    {
        var squares = (from file in "abcdefgh" from rank in "12345678" select $"{file}{rank}").ToArray();
        string[] suffixes = ["", "q", "r", "b", "n"];
        return
        [
            .. from origin in squares
               from target in squares
               where origin != target
               from suffix in suffixes
               select origin + target + suffix,
        ];
    }
}
