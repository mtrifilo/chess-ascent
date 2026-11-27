using System.Net;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Opponents;

/// <summary>The opponent inside the running application, with the Maia kind as the API configures it.</summary>
public sealed class OpponentHostingTests(PostgresFixture postgres)
{
    private const string StandardStartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static readonly string[] ShortGame = ["e2e4", "e7e5", "g1f3"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutMaiaInstalledTheApiStartsReadinessFailsAndTheOpponentSaysWhy()
    {
        var missing = Directory.CreateTempSubdirectory("no-engines-");
        try
        {
            await using var api = Api(host => host.UseSetting(MaiaInstallation.EnginesDirectorySetting, missing.FullName));
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
        await using var api = Api(_ => { });
        using var client = api.CreateClient();
        await Eventually.TrueAsync(
            async () =>
            {
                using var response = await client.GetAsync("/health/ready", Token);
                return response.StatusCode == HttpStatusCode.OK;
            },
            "Maia to start; has scripts/setup-maia.sh been run?");
        var opponent = api.Services.GetRequiredService<IOpponent>();
        var rules = new ChessRules();

        foreach (var strength in OpponentStrengths.Offered)
        {
            // The opponent plays both sides, so each reply comes from a game
            // with a longer history than the last.
            var moves = new List<string>(ShortGame);
            for (var ply = 0; ply < 30; ply++)
            {
                if (rules.Load(StandardStartingFen, moves) is not GameLoaded { Game.Status: Ongoing })
                {
                    break;
                }

                var reply = await opponent.ChooseMoveAsync(moves, strength, Token);

                var move = reply.Value.Should().BeOfType<OpponentMove>($"at {strength}, after {string.Join(' ', moves)}").Subject;
                rules.Load(StandardStartingFen, [.. moves, move.Uci]).Value.Should().BeOfType<GameLoaded>();
                moves.Add(move.Uci);
            }
        }
    }

    private MaiaApi Api(Action<IWebHostBuilder> configure) => new(postgres, configure);

    /// <summary>
    /// The API as <see cref="ApiFactory"/> runs it, which also makes it wait
    /// its turn to start, but with the Maia kind kept.
    /// </summary>
    private sealed class MaiaApi : IAsyncDisposable
    {
        private readonly ApiFactory _factory;
        private readonly WebApplicationFactory<Program> _configured;

        public MaiaApi(PostgresFixture postgres, Action<IWebHostBuilder> configure)
        {
            _factory = new ApiFactory(postgres) { KeepsMaia = true };
            _configured = _factory.WithWebHostBuilder(configure);
        }

        public IServiceProvider Services => _configured.Services;

        public HttpClient CreateClient() => _configured.CreateClient();

        public async ValueTask DisposeAsync()
        {
            await _configured.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
