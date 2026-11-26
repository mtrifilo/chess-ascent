using System.Diagnostics;
using System.Net;
using ChessAscent.Api.Database;

namespace ChessAscent.Api.Tests.Features.Health;

public sealed class DatabaseReadinessTests
{
    [Fact]
    public async Task StoppedDatabaseFailsReadinessButNotLiveness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Its own server, because stopping the shared one would fail every
        // other test running at the same time.
        await using var postgres = new PostgresFixture();
        await postgres.InitializeAsync();
        await using var factory = new ApiFactory(postgres);
        using var client = factory.CreateClient();

        using (var before = await client.GetAsync("/health/ready", cancellationToken))
        {
            before.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await postgres.StopAsync();

        using var readiness = await client.GetAsync("/health/ready", cancellationToken);
        using var liveness = await client.GetAsync("/health", cancellationToken);

        readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnansweringDatabaseFailsReadinessWhenTheCheckTimesOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgresFixture();
        await postgres.InitializeAsync();
        await using var factory = new ApiFactory(postgres);
        using var client = factory.CreateClient();

        using (var before = await client.GetAsync("/health/ready", cancellationToken))
        {
            before.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await postgres.PauseAsync();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var readiness = await client.GetAsync("/health/ready", cancellationToken);
            stopwatch.Stop();

            readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            // A paused server never answers or refuses, so only the check's
            // timeout can end the wait: not before it, and well before the
            // 15 seconds an unbounded check would take.
            stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(DatabaseServices.ReadinessCheckTimeout)
                .And.BeLessThan(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await postgres.UnpauseAsync();
        }
    }
}
