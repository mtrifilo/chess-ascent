using System.Net;
using ChessAscent.Api.Features.Health;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChessAscent.Api.Tests.Features.Health;

// LEARN: WebApplicationFactory<Program> starts the real application from
// Program.cs, with its real services and middleware, on an in-memory server,
// and CreateClient() returns an HttpClient wired straight to it. It is the
// counterpart of @SpringBootTest with MockMvc or TestRestTemplate: no port is
// opened and no network is involved, but every request goes through the full
// pipeline.
//
// LEARN: IClassFixture<T> is xUnit's shared setup. xUnit creates a new
// instance of this class for every test (as JUnit 5 does by default), so the
// constructor is per-test setup. A class fixture is created once for all the
// tests in the class and handed to the constructor, which is how the one
// running application is shared, like JUnit's @BeforeAll.
//
// LEARN: The parameter list after the class name is a primary constructor.
// `factory` is in scope in every member, so there is no field to declare and
// no constructor body to write; Java records do the same, but only for
// records.
public sealed class HealthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointAnswers200WhenTheProcessServes(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be("Healthy");
    }

    [Fact]
    public async Task FailingReadinessCheckFailsReadinessButNotLiveness()
    {
        using var client = ClientWithFailingCheck(tags: [HealthEndpoints.ReadinessTag]);

        using var liveness = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
        readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task FailingCheckWithoutTheReadinessTagLeavesReadinessHealthy()
    {
        using var client = ClientWithFailingCheck(tags: []);

        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        readiness.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient ClientWithFailingCheck(string[] tags) =>
        factory.WithWebHostBuilder(host => host.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck("always-unhealthy", () => HealthCheckResult.Unhealthy(), tags)))
            .CreateClient();
}
