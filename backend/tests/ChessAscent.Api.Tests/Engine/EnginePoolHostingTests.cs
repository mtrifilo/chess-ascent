using System.Net;
using ChessAscent.Api.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>The pool inside the running application: configuration, readiness, and shutdown.</summary>
public sealed class EnginePoolHostingTests(PostgresFixture postgres)
{
    private const string Kind = "fake";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadinessReportsTheEngineOnceItRuns()
    {
        using var fake = new FakeEngineKind();
        await using var api = Api(FakeEngineKind.Settings(Kind, fake.Options()));
        using var client = api.CreateClient();

        await ReadinessIsOkAsync(client);
        var pool = api.Services.GetRequiredService<EnginePool>();
        var processId = await pool.RunAsync(Kind, lease => Task.FromResult(lease.Engine.ProcessId), Token);
        processId.Should().Be(fake.StartedProcessIds().Single());
    }

    [Fact]
    public async Task ProgramThatDoesNotExistStillLetsTheApiStartButFailsReadiness()
    {
        using var fake = new FakeEngineKind();
        var missing = Path.Combine(Path.GetTempPath(), "no-such-engine-" + Guid.NewGuid());
        await using var api = Api(FakeEngineKind.Settings(Kind, fake.Options(executablePath: missing)));
        using var client = api.CreateClient();
        var pool = api.Services.GetRequiredService<EnginePool>();
        var call = () => pool.RunAsync(Kind, lease => Task.FromResult(0), Token);

        // The pool gives up after its start attempts; the API goes on serving.
        (await call.Should().ThrowExactlyAsync<EngineUnavailableException>()).Which.Message.Should().Contain("stopped trying");
        using var liveness = await client.GetAsync("/health", Token);
        using var readiness = await client.GetAsync("/health/ready", Token);

        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
        readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ReadinessRecoversAfterTheEngineIsKilled()
    {
        using var fake = new FakeEngineKind();
        await using var api = Api(FakeEngineKind.Settings(Kind, fake.Options()));
        using var client = api.CreateClient();
        await ReadinessIsOkAsync(client);
        var first = fake.StartedProcessIds().Single();

        FakeEngine.Kill(first);

        await fake.WaitForStartedAsync(2);
        await ReadinessIsOkAsync(client);
    }

    [Fact]
    public async Task TheHostedServiceStopsEveryEngineWithinTheShutdownTimeout()
    {
        using var fake = new FakeEngineKind("--ignore-quit");
        await using var api = Api(FakeEngineKind.Settings(Kind, fake.Options(processes: 2, exitTimeout: TimeSpan.FromMilliseconds(500))));
        using var client = api.CreateClient();
        await ReadinessIsOkAsync(client);
        var processIds = fake.StartedProcessIds();
        var service = api.Services.GetServices<IHostedService>().OfType<EnginePoolService>().Single();
        var shutdownTimeout = api.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout;
        using var deadline = new CancellationTokenSource(shutdownTimeout);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await service.StopAsync(deadline.Token);

        clock.Elapsed.Should().BeLessThan(shutdownTimeout);
        foreach (var processId in processIds)
        {
            (await FakeEngine.IsGoneAsync(processId, attempts: 1)).Should().BeTrue($"process {processId} was stopped by the hosted service");
        }
    }

    [Fact]
    public async Task ShuttingDownTheApplicationLeavesNoEngineProcess()
    {
        // One kind obeys quit; the other ignores it and has to be killed.
        using var obeys = new FakeEngineKind();
        using var ignores = new FakeEngineKind("--ignore-quit");
        var settings = FakeEngineKind.Settings("obeys", obeys.Options(processes: 2))
            .Concat(FakeEngineKind.Settings("ignores", ignores.Options(processes: 2, exitTimeout: TimeSpan.FromMilliseconds(500))))
            .ToDictionary();
        var api = Api(settings);
        using var client = api.CreateClient();
        await ReadinessIsOkAsync(client);
        var processIds = obeys.StartedProcessIds().Concat(ignores.StartedProcessIds()).ToArray();
        processIds.Should().HaveCount(4);

        await api.DisposeAsync();

        foreach (var processId in processIds)
        {
            (await FakeEngine.IsGoneAsync(processId, attempts: 1)).Should().BeTrue($"process {processId} was stopped before the host finished stopping");
        }
    }

    [Fact]
    public async Task BadSettingStopsStartUpAndNamesTheSetting()
    {
        using var fake = new FakeEngineKind();
        var settings = FakeEngineKind.Settings(Kind, fake.Options());
        settings["EnginePool:Kinds:fake:Processes"] = "0";
        settings.Remove("EnginePool:Kinds:fake:SearchTimeout");
        await using var api = Api(settings);

        var start = () => api.CreateClient();

        start.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().BeEquivalentTo(
                "EnginePool:Kinds:fake:Processes must be at least 1.",
                "EnginePool:Kinds:fake:SearchTimeout is required.");
        fake.StartedProcessIds().Should().BeEmpty();
    }

    [Fact]
    public async Task MisspeltSettingStopsStartUpAndNamesTheSetting()
    {
        using var fake = new FakeEngineKind();
        var settings = FakeEngineKind.Settings(Kind, fake.Options());
        settings["EnginePool:Kinds:fake:Proceses"] = "2";
        await using var api = Api(settings);

        var start = () => api.CreateClient();

        // The binder names the unknown key in an inner exception, which the
        // start-up error shows with the outer one.
        start.Should().Throw<InvalidOperationException>()
            .WithInnerException<InvalidOperationException>().WithMessage("*'Proceses'*");
    }

    [Fact]
    public async Task UnreadableWaitStopsStartUpAndNamesTheSetting()
    {
        using var fake = new FakeEngineKind();
        var settings = FakeEngineKind.Settings(Kind, fake.Options());
        settings["EnginePool:Kinds:fake:SearchTimeout"] = "soon";
        await using var api = Api(settings);

        var start = () => api.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithInnerException<InvalidOperationException>().WithMessage("*'EnginePool:Kinds:fake:SearchTimeout'*");
    }

    private EngineApi Api(IReadOnlyDictionary<string, string?> settings) => new(postgres, settings);

    private static Task ReadinessIsOkAsync(HttpClient client) =>
        Eventually.TrueAsync(
            async () =>
            {
                using var response = await client.GetAsync("/health/ready", Token);
                return response.StatusCode == HttpStatusCode.OK;
            },
            "readiness to answer 200");

    /// <summary>The API, as <see cref="ApiFactory"/> runs it, with engine settings of its own.</summary>
    private sealed class EngineApi(PostgresFixture postgres, IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(ApiFactory.ConnectionStringKey, postgres.ConnectionString);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
