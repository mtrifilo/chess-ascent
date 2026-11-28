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
    // checks. The normal run does it on every tenth position and on the last
    // one of each game; the seed shifts which positions those are, so the
    // games between them cover every stage of a game. The slow tier does it
    // at every position (EveryOtherMoveIsRefusedAtEveryPosition).
    private const int RefusalCheckInterval = 10;

    public static TheoryData<int> Seeds() => [.. RandomGames.Seeds];

    // The invariants below are only as strong as the games they run over.
    // This fails if a change to the seeds, the length limit or the engine
    // leaves the games without some way of ending, without castling or an
    // under-promotion, or without a position that offers an en passant
    // capture, so that a check would quietly cover less. Random play rarely
    // chooses that capture when it is offered, so these games check that an
    // offered capture is listed and can be played as listed (on a branch),
    // but no position after one; EnPassantTests and perft cover that.
    [Fact]
    public void TheGamesReachEveryWayAGameCanEndAndEverySpecialMove()
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
        sans.Should().Contain(san => san.Contains("=R", StringComparison.Ordinal) || san.Contains("=B", StringComparison.Ordinal)
            || san.Contains("=N", StringComparison.Ordinal), "some game under-promotes");
        all.Should().Contain(game => OffersEnPassant(game), "some position offers an en passant capture");
    }

    // A pawn capture onto the square FEN names as the en passant target is
    // an en passant capture: that square is always empty.
    private static bool OffersEnPassant(RandomGame recorded) =>
        RandomGames.Positions(recorded).Any(position =>
        {
            var target = position.Game.Fen.Split(' ')[3];
            return target != "-" && position.Game.LegalMoves().Any(move =>
                move.To.ToString() == target && move.San is [>= 'a' and <= 'h', 'x', ..]);
        });

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

        // RandomGames.Play stops early when the engine lists no move in an
        // ongoing game or refuses a move it listed; this reports that here,
        // with the seed, rather than as an error in the shared fixture.
        (recorded.Status.Value is not Ongoing || recorded.Moves.Count == RandomGames.MaxPlies)
            .Should().BeTrue("a game stops only when it is over or reaches the length limit, but {0} stopped while {1}",
                recorded.At(recorded.Moves.Count), recorded.Status);
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

    // The positions a rebuilt game records are compared with the ones a game
    // played move by move passes through.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void ARebuiltGameGivesThePositionAfterEachMoveAsPlayed(int seed)
    {
        var recorded = games[seed];

        var rebuilt = Games.Play([.. recorded.Moves]);

        var reached = RandomGames.Positions(recorded).Skip(1).Select(position => position.Game.Fen).ToList();
        rebuilt.PositionsAfterMoves.Should().Equal(reached, "in {0}", recorded.At(recorded.Moves.Count));
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
    public void EveryListedMoveIsPlayedAndOtherMovesAreRefusedAtSampledPositions(int seed)
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
                AssertEveryOtherMoveIsRefused(game, legal, context);
            }
        }
    }

    [Theory(Explicit = true)]
    [Trait("Category", "Slow")]
    [MemberData(nameof(Seeds))]
    public void EveryOtherMoveIsRefusedAtEveryPosition(int seed)
    {
        var recorded = games[seed];
        foreach (var (ply, game) in RandomGames.Positions(recorded))
        {
            AssertEveryOtherMoveIsRefused(game, game.LegalMoves(), recorded.At(ply));
        }
    }

    // These two helpers compare plainly, collect the mismatches and make one
    // assertion per position, because an assertion per move would cost more
    // than playing the move. An exception from the engine is recorded as a
    // mismatch too, so the failure still names the position and the move:
    // IChessGame promises that no member throws for bad input.
    private static void AssertEveryListedMoveIsPlayedAsListed(IChessGame game, IReadOnlyList<LegalMove> legal, GamePosition context)
    {
        var historyLength = game.History.Count;
        var mismatches = new List<string>();
        foreach (var move in legal)
        {
            var branch = game.Branch();
            PlayResult result;
            try
            {
                result = branch.TryPlay(move.Uci);
            }
            catch (Exception exception)
            {
                mismatches.Add($"{move.Uci} threw {exception.GetType().Name}: {exception.Message}");
                continue;
            }

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

    // The candidates are tried on a branch, so the replayed game is never
    // moved on. After any wrong answer the branch is replaced with a fresh
    // one, so each later candidate is still judged in the position under
    // test, and the report says when the branch had changed: that is the
    // sign of a move refused but applied anyway, rather than of the
    // candidate named.
    private static void AssertEveryOtherMoveIsRefused(IChessGame position, IReadOnlyList<LegalMove> legal, GamePosition context)
    {
        var fen = position.Fen;
        var historyLength = position.History.Count;
        var listed = legal.Select(move => move.Uci).ToHashSet(StringComparer.Ordinal);
        var status = position.Status;

        var probe = position.Branch();
        var wronglyHandled = new List<string>();
        foreach (var candidate in RandomGames.EveryUciMove)
        {
            if (listed.Contains(candidate))
            {
                continue;
            }

            PlayResult expected = status.Value is Ongoing ? new IllegalMove(candidate) : new GameAlreadyOver(status);
            string? wrong;
            try
            {
                var actual = probe.TryPlay(candidate);
                wrong = Equals(actual.Value, expected.Value) ? null : $"gave {actual}";
            }
            catch (Exception exception)
            {
                wrong = $"threw {exception.GetType().Name}: {exception.Message}";
            }

            if (wrong is not null)
            {
                var now = probe.Fen;
                var changed = now == fen ? "" : $" (the position had changed to {now})";
                wronglyHandled.Add($"{candidate} {wrong}{changed}");
                probe = position.Branch();
            }
        }

        wronglyHandled.Should().BeEmpty("only listed moves may be played in {0}", context);
        probe.Fen.Should().Be(fen, "a refused move leaves the game unchanged in {0}", context);
        probe.History.Should().HaveCount(historyLength);
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
    // Long enough for a fifty-move draw (100 half-moves without a capture or
    // pawn move) to arise late in a game, as it does in some of these.
    public const int MaxPlies = 400;

    // Fixed seeds, so a failure reproduces. A seed added here is a new game.
    // The rarest ending, threefold repetition, first comes at seed 29, so
    // the first 29 seeds are the fewest that end in every way a game can end
    // (TheGamesReachEveryWayAGameCanEndAndEverySpecialMove fails if they stop
    // doing so). Forty gives some margin over that; each seed costs about
    // half a second in the normal run.
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
    //
    // A game also stops when the engine lists no move although the game is
    // ongoing, or refuses a move it listed. Throwing here would fail every
    // test in the class with one fixture error; stopping lets the invariant
    // tests report it for this seed, with the moves that lead to it.
    private static RandomGame Play(int seed)
    {
        var random = new Random(seed);
        var game = Games.Rules.NewGame();
        var moves = new List<string>();

        try
        {
            while (moves.Count < MaxPlies && game.Status.Value is Ongoing)
            {
                var legal = game.LegalMoves();
                if (legal.Count == 0)
                {
                    break;
                }

                var choice = legal[random.Next(legal.Count)].Uci;
                if (game.TryPlay(choice) is not MovePlayed)
                {
                    break;
                }

                moves.Add(choice);
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The engine threw while playing seed {seed} after {moves.Count} half-moves: {string.Join(' ', moves)}", exception);
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
