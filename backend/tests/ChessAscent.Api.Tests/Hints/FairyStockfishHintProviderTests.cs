using ChessAscent.Api.Engine;
using ChessAscent.Api.Hints;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Chess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Hints;

/// <summary>
/// The Fairy-Stockfish hint provider against the fake engine, run by the
/// pool as the Fairy-Stockfish kind: what it sends before every search, what
/// it refuses to send, and what it does with a reply it cannot trust.
/// </summary>
public sealed class FairyStockfishHintProviderTests : IDisposable
{
    private static readonly string[] FoolsMate = ["f2f3", "e7e5", "g2g4", "d8h4"];

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fairy-stockfish-hints-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string TranscriptPath => Path.Combine(_directory.FullName, "transcript.txt");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task TheEnginesMoveIsTheHintWhenItIsLegal()
    {
        await using var hints = Start();

        var reply = await hints.Provider.FindBestMoveAsync(["g1f3", "g8f6"], Token);

        reply.Value.Should().Be(new HintMove("e2e4"));
    }

    [Fact]
    public async Task EveryRequestSetsOneThreadFullStrengthThreeLinesAndAnEmptyHashTableThenSearchesToDepthTwelve()
    {
        await using var hints = Start();

        await hints.Provider.FindBestMoveAsync([], Token);
        await hints.Provider.FindBestMoveAsync(["g1f3", "g8f6"], Token);

        // isready comes between the commands, from the client and the pool's
        // check, and says nothing about what was asked.
        string[] setUp =
        [
            "setoption name Threads value 1",
            "setoption name UCI_LimitStrength value false",
            "setoption name Skill Level value 20",
            "setoption name MultiPV value 3",
            "setoption name Clear Hash",
        ];
        (await TranscriptAsync()).Where(command => command != "isready").Should().Equal(
            ["uci", .. setUp, "position startpos", "go depth 12", .. setUp, "position startpos moves g1f3 g8f6", "go depth 12"]);
    }

    [Fact]
    public async Task MovesThatAreNotALegalGameAreRefusedBeforeTheEngineIsAsked()
    {
        // The pool is never started, so asking the engine could not answer
        // with anything but a failure, after the check-out timeout.
        await using var hints = Build();

        var illegal = await hints.Provider.FindBestMoveAsync(["e2e4", "e1e3", "g8f6"], Token);
        var malformed = await hints.Provider.FindBestMoveAsync(["e2e4", "e7e5 g1f3"], Token);
        var upperCase = await hints.Provider.FindBestMoveAsync(["E2E4"], Token);

        illegal.Value.Should().Be(new InvalidMoveInHistory(1, new IllegalMove("e1e3")));
        malformed.Value.Should().Be(new InvalidMoveInHistory(1, new MalformedMove("e7e5 g1f3")));
        upperCase.Value.Should().Be(new InvalidMoveInHistory(0, new MalformedMove("E2E4")));
        File.Exists(TranscriptPath).Should().BeFalse("no engine was started, let alone sent a position");
    }

    [Fact]
    public async Task FinishedGameIsNotSentToTheEngine()
    {
        await using var hints = Build();

        var reply = await hints.Provider.FindBestMoveAsync(FoolsMate, Token);

        reply.Value.Should().Be(new GameAlreadyOver(new Checkmate(Side.Black)));
    }

    // How a reply is made safe and cut short in the log is shared with Maia,
    // and MaiaOpponentTests covers it.
    [Theory]
    [InlineData("e7e5", "e7e5")]
    [InlineData("(none)", "(no move)")]
    [InlineData("", "(bestmove with no move)")]
    public async Task ReplyThatIsNotALegalMoveIsAnEngineFault(string reply, string logged)
    {
        await using var hints = Start("--bestmove", reply);
        var first = (await hints.Fake.WaitForStartedAsync(1)).Single();

        // White is to move, so e7e5 is a move for the wrong side: what this
        // engine answers after it stops at an illegal move in the list.
        var result = await hints.Provider.FindBestMoveAsync([], Token);

        result.Value.Should().BeOfType<HintFailed>().Which.Reason.Should().Contain("not a legal move");
        hints.ProviderLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().Contain("Engine fault: Fairy-Stockfish process").And.Contain($"replied {logged},");

        // The process that answered is not used again.
        await hints.Fake.WaitForStartedAsync(2);
        hints.PoolLog.Entries.Should().Contain(entry =>
            entry.Message.Contains($"replacing process {first}, because a caller reported it broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EngineThatIsNotInstalledFailsWithAReason()
    {
        var missing = Path.Combine(_directory.FullName, "no-such-engines", "fairy_stockfish", "fairy-stockfish");
        await using var hints = Start(fake => fake.Options(executablePath: missing, startAttempts: 1));

        var reply = await hints.Provider.FindBestMoveAsync([], Token);

        reply.Value.Should().BeOfType<HintFailed>()
            .Which.Reason.Should().Contain("\"fairy_stockfish\"").And.Contain("stopped trying");
        hints.ProviderLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task PoolWithoutTheKindGivesAFailureNotAnException()
    {
        var pool = new EnginePool(Options.Create(new EnginePoolOptions()), new ListLogger<EnginePool>(), new ListLogger<UciEngine>());
        await using (pool)
        {
            var provider = new FairyStockfishHintProvider(pool, new ChessRules(), new ListLogger<FairyStockfishHintProvider>());

            var reply = await provider.FindBestMoveAsync([], Token);

            reply.Value.Should().BeOfType<HintFailed>().Which.Reason.Should().Contain("\"fairy_stockfish\"");
        }
    }

    [Fact]
    public async Task ReplyThatComesTooLateIsAFailure()
    {
        await using var hints = Start(
            fake => fake.Options(searchTimeout: TimeSpan.FromMilliseconds(300)),
            "--search-ms", "5000");

        var reply = await hints.Provider.FindBestMoveAsync([], Token);

        reply.Value.Should().BeOfType<HintFailed>().Which.Reason.Should().Contain("bestmove");
    }

    [Fact]
    public async Task EngineThatExitsDuringTheSearchIsAFailure()
    {
        await using var hints = Start("--exit-on", "go");

        var reply = await hints.Provider.FindBestMoveAsync([], Token);

        reply.Value.Should().BeOfType<HintFailed>().Which.Reason.Should().Contain("exited");
    }

    [Fact]
    public async Task CancellationIsNotTurnedIntoAFailure()
    {
        await using var hints = Start();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var call = () => hints.Provider.FindBestMoveAsync([], cancelled.Token);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    private Hints Build(params string[] behaviour) => Build(fake => fake.Options(), behaviour);

    private Hints Build(Func<FakeEngineKind, EngineKindOptions> options, params string[] behaviour)
    {
        var fake = new FakeEngineKind(["--transcript", TranscriptPath, .. behaviour]);
        var poolOptions = new EnginePoolOptions();
        poolOptions.Kinds[FairyStockfishHintProvider.EngineKind] = options(fake);
        var poolLog = new ListLogger<EnginePool>();
        var pool = new EnginePool(Options.Create(poolOptions), poolLog, new ListLogger<UciEngine>());
        var providerLog = new ListLogger<FairyStockfishHintProvider>();
        return new Hints(fake, pool, new FairyStockfishHintProvider(pool, new ChessRules(), providerLog), poolLog, providerLog);
    }

    private Hints Start(params string[] behaviour) => Start(fake => fake.Options(), behaviour);

    private Hints Start(Func<FakeEngineKind, EngineKindOptions> options, params string[] behaviour)
    {
        var hints = Build(options, behaviour);
        hints.Pool.Start();
        return hints;
    }

    private async Task<string[]> TranscriptAsync() =>
        File.Exists(TranscriptPath) ? await File.ReadAllLinesAsync(TranscriptPath, Token) : [];

    private sealed record Hints(
        FakeEngineKind Fake,
        EnginePool Pool,
        FairyStockfishHintProvider Provider,
        ListLogger<EnginePool> PoolLog,
        ListLogger<FairyStockfishHintProvider> ProviderLog) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Pool.DisposeAsync();
            Fake.Dispose();
        }
    }
}
