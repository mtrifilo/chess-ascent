using System.Net;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Engine;
using ChessAscent.Api.Tests.Features.Games;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task TheOpponentIsMaiaUnlessTheSettingSaysOtherwise()
    {
        await using var api = new ApiFactory(postgres);

        api.Services.GetRequiredService<IOpponent>().Should().BeOfType<MaiaOpponent>();
    }

    // False is what the refusal outside Development tells an operator to set.
    [Fact]
    public async Task SetToFalseOutsideDevelopmentTheOpponentIsMaia()
    {
        await using var factory = new ApiFactory(postgres);
        await using var api = factory.WithWebHostBuilder(host => host
            .UseEnvironment("Production")
            .UseSetting(OpponentServices.UseFirstLegalMoveSetting, "false"));

        api.Services.GetRequiredService<IOpponent>().Should().BeOfType<MaiaOpponent>();
    }

    [Fact]
    public async Task AValueThatIsNotTrueOrFalseStopsStartUpAndNamesTheSetting()
    {
        await using var factory = new ApiFactory(postgres);
        await using var api = factory.WithWebHostBuilder(host => host.UseSetting(OpponentServices.UseFirstLegalMoveSetting, "yes"));

        var start = () => api.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage($"*{OpponentServices.UseFirstLegalMoveSetting}*");
    }

    [Fact]
    public async Task InDevelopmentTheSettingMakesTheFirstLegalMoveTheComputersReplyAndSaysSoOnce()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new ApiFactory(postgres);
        await using var api = factory.WithWebHostBuilder(host => host
            .UseSetting(OpponentServices.UseFirstLegalMoveSetting, "true")
            .ConfigureLogging(logging => logging.AddProvider(logs).AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = await api.SignedInClientAsync();
        var game = await client.CreateComputerGameAsync("white");

        var afterFirst = await client.PlayAsync(game.Id, "e2e4");

        api.Services.GetRequiredService<IOpponent>().Should().BeOfType<FirstLegalMoveOpponent>();
        afterFirst.Moves.Notation().Should().Equal((1, "e2e4", "e4"), (2, "a7a5", "a5"));
        logs.Messages.Where(message => message.Contains(OpponentServices.UseFirstLegalMoveSetting, StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("first legal move");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task OutsideDevelopmentTheSettingStopsStartUpAndNamesItself(string environment)
    {
        await using var factory = new ApiFactory(postgres);
        await using var api = factory.WithWebHostBuilder(host => host
            .UseEnvironment(environment)
            .UseSetting(OpponentServices.UseFirstLegalMoveSetting, "true"));

        var start = () => api.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage($"{OpponentServices.UseFirstLegalMoveSetting} is true, but the environment is {environment}.*");
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
