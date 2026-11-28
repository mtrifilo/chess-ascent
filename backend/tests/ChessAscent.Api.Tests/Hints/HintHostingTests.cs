using System.Net;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Hints;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Api.Tests.Features.Games;
using ChessAscent.Chess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Hints;

/// <summary>The hint provider inside the running application, with the Fairy-Stockfish kind as the API configures it.</summary>
public sealed class HintHostingTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutFairyStockfishInstalledTheApiStartsReadinessFailsAndTheHintSaysWhy()
    {
        var missing = Directory.CreateTempSubdirectory("no-engines-");
        try
        {
            await using var factory = new ApiFactory(postgres) { KeptEngineKinds = [FairyStockfishHintProvider.EngineKind] };
            await using var api = factory.WithWebHostBuilder(host => host.UseSetting(EngineInstallation.EnginesDirectorySetting, missing.FullName));
            using var client = api.CreateClient();
            var hints = api.Services.GetRequiredService<IHintProvider>();
            var kind = api.Services.GetRequiredService<IOptions<EnginePoolOptions>>().Value.Kinds[FairyStockfishHintProvider.EngineKind];
            kind.ExecutablePath.Should().StartWith(missing.FullName, "the engines directory setting points the kind there");

            var reply = await hints.FindBestMoveAsync(["e2e4", "e7e5", "g1f3"], Token);

            reply.Value.Should().BeOfType<HintFailed>().Which.Reason.Should().Contain("\"fairy_stockfish\"");
            using var liveness = await client.GetAsync("/health", Token);
            using var readiness = await client.GetAsync("/health/ready", Token);
            liveness.StatusCode.Should().Be(HttpStatusCode.OK);
            readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            missing.Delete(recursive: true);
        }
    }

    // Three positions from games between two weak settings of the engine, in
    // each of which one move is far better than any other: by Stockfish 19
    // at depth 16, the best move scored at least 2 pawns more than the
    // second best. Stockfish 19 at depth 22 chose the same move. The margins
    // below are from that depth-16 search. A different hint here is a
    // regression to investigate, not test data to update.
    public static TheoryData<string, string[], string> TacticalPositions() => new()
    {
        {
            // Black to move; the move is 4.5 pawns better than the next best.
            "rnbqkb1r/1p3ppp/p2p1n2/3Np1B1/3NP3/8/PPP2PPP/R2QKB1R b KQkq - 1 7",
            ["e2e4", "c7c5", "g1f3", "d7d6", "d2d4", "c5d4", "f3d4", "g8f6", "b1c3", "a7a6", "c1g5", "e7e5", "c3d5"],
            "e5d4"
        },
        {
            // Black to move; 5.5 pawns better than the next best.
            "r4rk1/4q1p1/p2pNn1p/1p2p3/3nP3/2PQB3/PP3PPP/R3KB1R b KQ - 0 16",
            [
                "e2e4", "c7c5", "g1f3", "d7d6", "d2d4", "c5d4", "f3d4", "g8f6", "b1c3", "a7a6", "c1g5", "e7e5", "c3d5", "b7b5",
                "d4f5", "b8c6", "f5h6", "c6d4", "c2c3", "f8e7", "d5e7", "d8e7", "h6f7", "e8g8", "f7d8", "c8g4", "d1d3", "h7h6",
                "g5e3", "g4e6", "d8e6",
            ],
            "d4e6"
        },
        {
            // White to move, with a forced mate that no other move keeps.
            // The moves include a promotion (d7e8q).
            "1rb3k1/5p2/pq2pQ1p/3p1B1P/5N2/P5P1/2P2P2/5K1R w - - 2 31",
            [
                "e2e4", "e7e6", "d2d4", "d7d5", "b1c3", "g8f6", "e4e5", "f6d7", "h2h4", "c7c6", "h4h5", "h7h6", "a2a3", "c6c5",
                "c3b5", "a7a6", "b5d6", "f8d6", "e5d6", "e8g8", "d4c5", "f8e8", "b2b4", "d8f6", "a1b1", "b7b6", "c1b2", "f6f5",
                "f1d3", "f5g5", "g2g3", "b6c5", "g1h3", "g5d8", "d1g4", "d7f6", "g4h4", "b8c6", "b2f6", "g7f6", "d6d7", "f6f5",
                "d7e8q", "d8e8", "h4f6", "e8f8", "h3f4", "c6d4", "e1f1", "c5b4", "f6d4", "b4b3", "b1b3", "f8d6", "d3f5", "d6c6",
                "b3b6", "c6b6", "d4f6", "a8b8",
            ],
            "f5h7"
        },
    };

    // LEARN: The real engine, opt-in in the same way as the chess project's
    // slow tier (see PerftTests): left out of every run unless explicit
    // tests are asked for. Run scripts/setup-fairy-stockfish.sh first, then,
    // from backend/:
    // dotnet test --project tests/ChessAscent.Api.Tests --explicit only --filter-trait "Category=FairyStockfish"
    [Theory(Explicit = true)]
    [Trait("Category", "FairyStockfish")]
    [MemberData(nameof(TacticalPositions))]
    public async Task InstalledFairyStockfishFindsTheTacticalMoveEveryTime(string fen, string[] moves, string expected)
    {
        GameData.Rules.Load(GameData.StartingFen, moves).Value.Should().BeOfType<GameLoaded>()
            .Which.Game.Fen.Should().Be(fen, "the moves lead to the test position");
        await using var api = new ApiFactory(postgres) { KeptEngineKinds = [FairyStockfishHintProvider.EngineKind] };
        using var client = api.CreateClient();
        await Eventually.TrueAsync(
            async () =>
            {
                using var response = await client.GetAsync("/health/ready", Token);
                return response.StatusCode == HttpStatusCode.OK;
            },
            "Fairy-Stockfish to start; has scripts/setup-fairy-stockfish.sh been run?");
        var hints = api.Services.GetRequiredService<IHintProvider>();

        // A search of another game between the two asks, so that the second
        // finds the process as another request left it.
        var first = await hints.FindBestMoveAsync(moves, Token);
        var other = await hints.FindBestMoveAsync(["d2d4", "d7d5", "c2c4"], Token);
        var second = await hints.FindBestMoveAsync(moves, Token);

        first.Value.Should().Be(new HintMove(expected));
        other.Value.Should().BeOfType<HintMove>();
        second.Value.Should().Be(new HintMove(expected));
    }
}
