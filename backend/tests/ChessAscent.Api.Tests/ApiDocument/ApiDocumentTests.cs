using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace ChessAscent.Api.Tests.ApiDocument;

// WebApplicationFactory runs the API in the Development environment unless
// told otherwise, so these tests see the document and the explorer as
// `dotnet run` serves them.
public sealed class ApiDocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string DocumentPath = "/openapi/v1.json";
    private const string ExplorerPath = "/scalar/";

    [Fact]
    public async Task DocumentDeclaresOpenApi31()
    {
        var document = await GetDocumentAsync();

        document["openapi"]!.GetValue<string>().Should().StartWith("3.1.");
    }

    [Fact]
    public async Task DocumentDescribesTheVersionResponse()
    {
        var document = await GetDocumentAsync();

        var ok = document["paths"]!["/version"]!["get"]!["responses"]!["200"]!;
        ok["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/AppVersionResponse");
    }

    [Fact]
    public async Task DocumentDescribesReadinessWithTheBodiesEachStatusCarries()
    {
        var document = await GetDocumentAsync();

        var responses = document["paths"]!["/health/ready"]!["get"]!["responses"]!.AsObject();
        responses.Select(response => response.Key).Should().Equal("200", "503");
        PlainTextValues(responses["200"]!).Should().Equal("Healthy", "Degraded");
        PlainTextValues(responses["503"]!).Should().Equal("Unhealthy");
    }

    [Fact]
    public async Task DocumentListsExactlyTheFourAccountEndpoints()
    {
        var document = await GetDocumentAsync();

        var operations = document["paths"]!.AsObject()
            .Where(path => path.Key.StartsWith("/auth", StringComparison.Ordinal))
            .SelectMany(path => path.Value!.AsObject().Select(operation => $"{operation.Key} {path.Key}"));
        operations.Should().BeEquivalentTo(
            "post /auth/register", "post /auth/sign-in", "post /auth/sign-out", "get /auth/me");
    }

    [Fact]
    public async Task DocumentListsNoOtherPathsThanTheApisOwn()
    {
        var document = await GetDocumentAsync();

        document["paths"]!.AsObject().Select(path => path.Key).Should().BeEquivalentTo(
            "/auth/register", "/auth/sign-in", "/auth/sign-out", "/auth/me", "/health/ready", "/version");
    }

    [Fact]
    public async Task ExplorerIsServedInDevelopment()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ExplorerPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Theory]
    [InlineData(DocumentPath)]
    [InlineData(ExplorerPath)]
    public async Task OutsideDevelopmentNeitherTheDocumentNorTheExplorerIsServed(string path)
    {
        using var production = factory.WithWebHostBuilder(host => host.UseEnvironment("Production"));
        using var client = production.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<JsonNode> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonNode.Parse(body)!;
    }

    private static IEnumerable<string> PlainTextValues(JsonNode response) =>
        response["content"]!["text/plain"]!["schema"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>());
}
