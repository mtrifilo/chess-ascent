using System.Net;

namespace ChessAscent.Api.Tests.Features.Health;

public sealed class DatabaseReadinessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ReadinessAnswers200WhileTheDatabaseAnswers()
    {
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        readiness.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StoppedDatabaseFailsReadinessButNotLiveness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Its own server, because stopping the shared one would fail every
        // other test running at the same time.
        await using var postgres = new PostgresFixture();
        await postgres.InitializeAsync();
        await using var stoppingFactory = new ApiFactory(postgres);
        using var client = stoppingFactory.CreateClient();

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
}
