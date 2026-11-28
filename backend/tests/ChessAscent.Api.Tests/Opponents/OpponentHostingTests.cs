using System.Net;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Api.Tests.Features.Games;
using ChessAscent.Chess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Opponents;

/// <summary>The opponent inside the running application, with the Maia kind as the API configures it.</summary>
public sealed class OpponentHostingTests(PostgresFixture postgres)
{
    private static readonly string[] ShortGame = ["e2e4", "e7e5", "g1f3"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutMaiaInstalledTheApiStartsReadinessFailsAndTheOpponentSaysWhy()
    {
        var missing = Directory.CreateTempSubdirectory("no-engines-");
        try
        {
            await using var factory = new ApiFactory(postgres) { KeptEngineKinds = [MaiaOpponent.EngineKind] };
            await using var api = factory.WithWebHostBuilder(host => host.UseSetting(EngineInstallation.EnginesDirectorySetting, missing.FullName));
            using var client = api.CreateClient();
            var opponent = api.Services.GetRequiredService<IOpponent>();
            var maia = api.Services.GetRequiredService<IOptions<EnginePoolOptions>>().Value.Kinds[MaiaOpponent.EngineKind];
            maia.ExecutablePath.Should().StartWith(missing.FullName, "the engines directory setting points the kind there");

            var reply = await opponent.ChooseMoveAsync(ShortGame, 800, Token);

            reply.Value.Should().BeOfType<OpponentFailed>().Which.Reason.Should().Contain("\"maia\"");
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

    // LEARN: The real engine, opt-in in the same way as the chess project's
    // slow tier (see PerftTests): left out of every run unless explicit
    // tests are asked for. Run the set-up script first, then, from backend/:
    // dotnet test --project tests/ChessAscent.Api.Tests --explicit only --filter-trait "Category=Maia"
    [Fact(Explicit = true)]
    [Trait("Category", "Maia")]
    public async Task InstalledMaiaGivesALegalReplyAtEveryPreset()
    {
        await using var api = new ApiFactory(postgres) { KeptEngineKinds = [MaiaOpponent.EngineKind] };
        using var client = api.CreateClient();
        await Eventually.TrueAsync(
            async () =>
            {
                using var response = await client.GetAsync("/health/ready", Token);
                return response.StatusCode == HttpStatusCode.OK;
            },
            "Maia to start; has scripts/setup-maia.sh been run?");
        var opponent = api.Services.GetRequiredService<IOpponent>();

        foreach (var strength in OpponentStrengths.Offered)
        {
            // The opponent plays both sides, so each reply comes from a game
            // with a longer history than the last.
            var moves = new List<string>(ShortGame);
            for (var ply = 0; ply < 30; ply++)
            {
                if (GameData.Rules.Load(GameData.StartingFen, moves) is not GameLoaded { Game.Status: Ongoing })
                {
                    break;
                }

                var reply = await opponent.ChooseMoveAsync(moves, strength, Token);

                var move = reply.Value.Should().BeOfType<OpponentMove>($"at {strength}, after {string.Join(' ', moves)}").Subject;
                GameData.Rules.Load(GameData.StartingFen, [.. moves, move.Uci]).Value.Should().BeOfType<GameLoaded>();
                moves.Add(move.Uci);
            }
        }
    }
}
