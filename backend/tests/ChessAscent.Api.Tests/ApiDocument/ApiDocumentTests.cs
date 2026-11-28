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

    // Exactly the API's own operations: in particular the four account
    // endpoints, and none of Identity's ready-made ones.
    [Fact]
    public async Task DocumentListsExactlyTheApisOperations()
    {
        var document = await GetDocumentAsync();

        var operations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => $"{operation.Key} {path.Key}"));
        operations.Should().BeEquivalentTo(
            "post /auth/register", "post /auth/sign-in", "post /auth/sign-out", "get /auth/me",
            "post /games", "get /games/{id}", "post /games/{id}/moves",
            "get /health/ready", "get /version");
    }

    // Every response the game endpoints declare, including the 401 the
    // group's authorization gives and the 403 of the cross-site refusal.
    // The 415 that body binding gives to a POST whose body is not sent as
    // JSON is not declared, here or on the account endpoints: the typed
    // client always sends JSON. The move endpoint's remarks list it.
    [Theory]
    [InlineData("/games", "post", new[] { "201", "400", "401", "403" })]
    [InlineData("/games/{id}", "get", new[] { "200", "401", "404", "500" })]
    [InlineData("/games/{id}/moves", "post", new[] { "200", "400", "401", "403", "404", "409", "422", "500" })]
    public async Task DocumentListsEveryResponseOfTheGameEndpoints(string path, string method, string[] statuses)
    {
        var document = await GetDocumentAsync();

        document["paths"]![path]![method]!["responses"]!.AsObject().Select(response => response.Key)
            .Should().BeEquivalentTo(statuses);
    }

    // Number handling is strict, so an integer is described as a JSON
    // integer only, not also as a string of digits.
    [Theory]
    [InlineData("MoveResponse", "halfMoveNumber", new[] { "integer" })]
    [InlineData("OpponentResponse", "strength", new[] { "null", "integer" })]
    [InlineData("ProblemDetails", "status", new[] { "null", "integer" })]
    public async Task DocumentDescribesIntegersAsJsonIntegersOnly(string schema, string property, string[] types)
    {
        var document = await GetDocumentAsync();

        var described = document["components"]!["schemas"]![schema]!["properties"]![property]!.AsObject();
        var type = described["type"]!;
        (type is JsonArray array ? array.Select(item => item!.GetValue<string>()) : [type.GetValue<string>()])
            .Should().BeEquivalentTo(types);
        described.ContainsKey("pattern").Should().BeFalse();
    }

    // Enumerations are written by name, so the document lists the names, and
    // these are part of the contract: renaming a member fails here. Null is
    // never one of them, even for an enum the API only ever sends as a
    // nullable property; the property says it may be null.
    [Theory]
    [InlineData("Side", new[] { "white", "black" })]
    [InlineData("OpponentKind", new[] { "none", "computer" })]
    [InlineData("GameProgress", new[] { "ongoing", "finished" })]
    [InlineData("GameResult", new[] { "whiteWins", "blackWins", "draw" })]
    [InlineData("GameEndReason", new[] { "checkmate", "stalemate", "threefoldRepetition", "fiftyMoveRule", "deadPosition", "resignation" })]
    [InlineData("PromotionPiece", new[] { "queen", "rook", "bishop", "knight" })]
    public async Task DocumentListsEachEnumerationsCamelCaseNamesAndNothingElse(string schema, string[] names)
    {
        var document = await GetDocumentAsync();

        var members = document["components"]!["schemas"]![schema]!["enum"]!.AsArray();
        members.Select(member => member?.ToJsonString()).Should().Equal(names.Select(name => $"\"{name}\""));
    }

    // A request field that validation requires is never null, though it is
    // nullable in C# so that validation can see it was missing. A response
    // field that can be null says so. The document writes "may be null" in
    // two ways: oneOf with a null schema, for a reference to a shared
    // schema such as an enum, and a type list that includes "null", for a
    // plain value such as an integer.
    //
    // Each enum property must also refer to its enum's shared schema, which
    // is what gives the front end a named type for it, and no property may
    // use a form this test does not read, so a schema of another shape fails
    // here rather than passing unread.
    [Theory]
    [InlineData("CreateGameRequest", "playerSide", false, "Side")]
    [InlineData("OpponentRequest", "kind", false, "OpponentKind")]
    [InlineData("GameResponse", "result", true, "GameResult")]
    [InlineData("GameResponse", "endReason", true, "GameEndReason")]
    [InlineData("GameResponse", "playerSide", false, "Side")]
    [InlineData("OpponentResponse", "strength", true, null)]
    public async Task DocumentAllowsNullOnlyWhereTheApiSendsOrAcceptsIt(
        string schema, string property, bool nullable, string? enumSchema)
    {
        var document = await GetDocumentAsync();

        var described = document["components"]!["schemas"]![schema]!["properties"]![property]!.AsObject();
        described.ContainsKey("anyOf").Should().BeFalse();
        var choices = described["oneOf"] as JsonArray ?? [];
        var nullInOneOf = choices.Any(choice => choice?["type"]?.GetValue<string>() == "null");
        var nullInTypes = described["type"] is JsonArray types
            && types.Any(type => type?.GetValue<string>() == "null");
        (nullInOneOf || nullInTypes).Should().Be(nullable);

        var references = choices.Select(choice => choice?["$ref"]).Append(described["$ref"]).OfType<JsonNode>();
        if (enumSchema is not null)
        {
            references.Should().ContainSingle("an enum property refers to its enum's shared schema")
                .Which.GetValue<string>().Should().Be($"#/components/schemas/{enumSchema}");
            document["components"]!["schemas"]![enumSchema]!["enum"].Should().NotBeNull();
        }
        else
        {
            described["type"].Should().NotBeNull("a plain value states its type");
        }
    }

    // Every response the account endpoints declare, including those that
    // come from validation and from the refusal of cross-site requests (for
    // the undeclared 415, see the game endpoints' test above).
    [Theory]
    [InlineData("/auth/register", "post", new[] { "200", "400", "403", "409" })]
    [InlineData("/auth/sign-in", "post", new[] { "200", "400", "401", "403" })]
    [InlineData("/auth/sign-out", "post", new[] { "204", "403" })]
    [InlineData("/auth/me", "get", new[] { "200", "401" })]
    public async Task DocumentListsEveryResponseOfTheAccountEndpoints(string path, string method, string[] statuses)
    {
        var document = await GetDocumentAsync();

        document["paths"]![path]![method]!["responses"]!.AsObject().Select(response => response.Key)
            .Should().BeEquivalentTo(statuses);
    }

    // The cross-site refusal (Security/CrossSiteRequests.cs) applies to every
    // method but the safe ones, so every such operation must declare its 403.
    [Fact]
    public async Task EveryOperationThatCanChangeStateListsTheCrossSiteRefusal()
    {
        var document = await GetDocumentAsync();
        string[] safe = ["get", "head", "options", "trace", "query"];

        var unsafeOperations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject())
            .Where(operation => !safe.Contains(operation.Key))
            .ToList();

        unsafeOperations.Should().NotBeEmpty();
        unsafeOperations.Should().AllSatisfy(operation =>
            operation.Value!["responses"]!.AsObject().ContainsKey("403").Should().BeTrue());
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
