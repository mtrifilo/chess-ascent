using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChessAscent.Api.Tests;

public sealed class ProblemDetailsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ThrowingPath = "/test-only/throw";
    private const string ExceptionMessage = "secret detail that must not reach the client";

    [Fact]
    public async Task UnknownPathAnswersWithA404ProblemDetails()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/no-such-path", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ShouldBeProblemDetailsAsync(response, 404);
    }

    [Fact]
    public async Task UnhandledExceptionAnswersWithA500ProblemDetailsThatHidesTheException()
    {
        using var throwingFactory = factory.WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
                services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>()));
        using var client = throwingFactory.CreateClient();

        using var response = await client.GetAsync(ThrowingPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await ShouldBeProblemDetailsAsync(response, 500);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(ExceptionMessage);
    }

    private static async Task ShouldBeProblemDetailsAsync(HttpResponseMessage response, int status)
    {
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            TestContext.Current.CancellationToken);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(status);
    }

    // Adds a path that throws, behind the application's own middleware, so the
    // test exercises the exception handler Program.cs configures. A startup
    // filter's middleware added after next(app) runs after the application's.
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowingPath, branch => branch.Run(_ =>
                throw new InvalidOperationException(ExceptionMessage)));
        };
    }
}
