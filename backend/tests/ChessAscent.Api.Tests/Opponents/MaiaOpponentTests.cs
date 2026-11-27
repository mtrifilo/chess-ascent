using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Chess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Opponents;

/// <summary>
/// The Maia opponent against the fake engine, run by the pool as the Maia
/// kind: what it sends before every move, what it refuses to send, and what
/// it does with a reply it cannot trust.
/// </summary>
public sealed class MaiaOpponentTests : IDisposable
{
    private static readonly string[] FoolsMate = ["f2f3", "e7e5", "g2g4", "d8h4"];

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("maia-opponent-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string TranscriptPath => Path.Combine(_directory.FullName, "transcript.txt");

    public void Dispose() => _directory.Delete(recursive: true);

    [Theory]
    [MemberData(nameof(Presets))]
    public async Task EveryPresetGetsTheEnginesMoveWhenItIsLegal(int strength)
    {
        await using var maia = Start();

        var reply = await maia.Opponent.ChooseMoveAsync([], strength, Token);

        reply.Value.Should().Be(new OpponentMove("e2e4"));
    }

    [Fact]
    public async Task EveryRequestSetsBothRatingsTheTemperatureAndOneLineBeforeItAsks()
    {
        await using var maia = Start();

        await maia.Opponent.ChooseMoveAsync([], 600, Token);
        await maia.Opponent.ChooseMoveAsync(["g1f3", "g8f6"], 1400, Token);

        // isready comes between the commands, from the client and the pool's
        // check, and says nothing about what was asked.
        (await TranscriptAsync()).Where(command => command != "isready").Should().Equal(
            "uci",
            "setoption name SelfElo value 600",
            "setoption name OppoElo value 600",
            "setoption name Temperature value 1.0",
            "setoption name MultiPV value 1",
            "position startpos",
            "go nodes 1",
            "setoption name SelfElo value 1400",
            "setoption name OppoElo value 1400",
            "setoption name Temperature value 1.0",
            "setoption name MultiPV value 1",
            "position startpos moves g1f3 g8f6",
            "go nodes 1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(599)]
    [InlineData(700)]
    [InlineData(1500)]
    [InlineData(-600)]
    public async Task StrengthOtherThanAPresetIsRefusedBeforeTheEngineIsAsked(int strength)
    {
        // The pool is never started, so asking the engine could not answer
        // with anything but a failure, after the check-out timeout.
        await using var maia = Build();

        var reply = await maia.Opponent.ChooseMoveAsync([], strength, Token);

        reply.Value.Should().Be(new StrengthNotOffered(strength));
    }

    [Fact]
    public async Task MovesThatAreNotALegalGameAreRefusedBeforeTheEngineIsAsked()
    {
        await using var maia = Build();

        var illegal = await maia.Opponent.ChooseMoveAsync(["e2e4", "e7e4"], 800, Token);
        var malformed = await maia.Opponent.ChooseMoveAsync(["e2e4", "e7e5 g1f3"], 800, Token);

        illegal.Value.Should().Be(new InvalidMoveInHistory(1, new IllegalMove("e7e4")));
        malformed.Value.Should().Be(new InvalidMoveInHistory(1, new MalformedMove("e7e5 g1f3")));
    }

    [Fact]
    public async Task FinishedGameIsNotSentToTheEngine()
    {
        await using var maia = Build();

        var reply = await maia.Opponent.ChooseMoveAsync(FoolsMate, 800, Token);

        reply.Value.Should().Be(new GameAlreadyOver(new Checkmate(Side.Black)));
    }

    [Theory]
    [InlineData("e7e5", "e7e5")]
    [InlineData("0000", "(no move)")]
    [InlineData("e2e4\u001b[31m", "e2e4?[31m")]
    [InlineData("a1a2a3a4a5a6a7a8a9", "a1a2a3a4a5a6a7a8...")]
    public async Task ReplyThatIsNotALegalMoveIsAnEngineFault(string reply, string logged)
    {
        await using var maia = Start("--bestmove", reply);
        var first = (await maia.Fake.WaitForStartedAsync(1)).Single();

        var result = await maia.Opponent.ChooseMoveAsync([], 800, Token);

        result.Value.Should().BeOfType<OpponentFailed>();
        maia.OpponentLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().Contain("Engine fault").And.Contain($"replied {logged},");

        // The process that answered is not used again.
        await maia.Fake.WaitForStartedAsync(2);
        maia.PoolLog.Entries.Should().Contain(entry =>
            entry.Message.Contains($"replacing process {first}, because a caller reported it broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpponentThatIsNotInstalledFailsWithAReason()
    {
        var missing = Path.Combine(_directory.FullName, "no-such-engines", "maia", "venv", "bin", "python");
        await using var maia = Start(fake => fake.Options(executablePath: missing, startAttempts: 1));

        var reply = await maia.Opponent.ChooseMoveAsync([], 800, Token);

        reply.Value.Should().BeOfType<OpponentFailed>()
            .Which.Reason.Should().Contain("\"maia\"").And.Contain("stopped trying");
        maia.OpponentLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ReplyThatComesTooLateIsAFailure()
    {
        await using var maia = Start(
            fake => fake.Options(searchTimeout: TimeSpan.FromMilliseconds(300)),
            "--search-ms", "5000");

        var reply = await maia.Opponent.ChooseMoveAsync([], 800, Token);

        reply.Value.Should().BeOfType<OpponentFailed>().Which.Reason.Should().Contain("bestmove");
    }

    [Fact]
    public async Task CancellationIsNotTurnedIntoAFailure()
    {
        await using var maia = Start();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var call = () => maia.Opponent.ChooseMoveAsync([], 800, cancelled.Token);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    public static TheoryData<int> Presets() => [.. OpponentStrengths.Offered];

    private Maia Build(params string[] behaviour) => Build(fake => fake.Options(), behaviour);

    private Maia Build(Func<FakeEngineKind, EngineKindOptions> options, params string[] behaviour)
    {
        var fake = new FakeEngineKind(["--transcript", TranscriptPath, .. behaviour]);
        var poolOptions = new EnginePoolOptions();
        poolOptions.Kinds[MaiaOpponent.EngineKind] = options(fake);
        var poolLog = new ListLogger<EnginePool>();
        var pool = new EnginePool(Options.Create(poolOptions), poolLog, new ListLogger<UciEngine>());
        var opponentLog = new ListLogger<MaiaOpponent>();
        return new Maia(fake, pool, new MaiaOpponent(pool, new ChessRules(), opponentLog), poolLog, opponentLog);
    }

    private Maia Start(params string[] behaviour) => Start(fake => fake.Options(), behaviour);

    private Maia Start(Func<FakeEngineKind, EngineKindOptions> options, params string[] behaviour)
    {
        var maia = Build(options, behaviour);
        maia.Pool.Start();
        return maia;
    }

    private async Task<string[]> TranscriptAsync() =>
        File.Exists(TranscriptPath) ? await File.ReadAllLinesAsync(TranscriptPath, Token) : [];

    private sealed record Maia(
        FakeEngineKind Fake,
        EnginePool Pool,
        MaiaOpponent Opponent,
        ListLogger<EnginePool> PoolLog,
        ListLogger<MaiaOpponent> OpponentLog) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Pool.DisposeAsync();
            Fake.Dispose();
        }
    }
}
