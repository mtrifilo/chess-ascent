using System.Net;
using System.Text.Json.Nodes;
using ChessAscent.Api.Features.Accounts;
using Microsoft.AspNetCore.Http;

namespace ChessAscent.Api.Tests.Features.Accounts;

// Each test's HttpClient keeps the cookies the API sets, as a browser would,
// so a new client is a browser that has never signed in.
public sealed class AccountEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task RegisterCreatesTheAccountAndSignsThePlayerIn()
    {
        using var client = factory.CreateClient();
        var email = Accounts.NewEmail();

        using var response = await client.RegisterAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        body!.Email.Should().Be(email);
        (await client.GetSignedInEmailAsync()).Should().Be(email);
    }

    [Fact]
    public async Task RegisterRefusesAnEmailThatAlreadyHasAnAccountWhateverItsCase()
    {
        var email = await factory.RegisterAccountAsync();

        using var second = factory.CreateClient();
        using var response = await second.RegisterAsync(email.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Detail.Should().Be(AccountEndpoints.DuplicateEmailDetail);
        response.SessionCookieHeader().Should().BeNull();
    }

    [Fact]
    public async Task RegisterReportsBrokenPasswordRulesUnderThePasswordField()
    {
        using var client = factory.CreateClient();

        using var response = await client.RegisterAsync(Accounts.NewEmail(), password: "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal(nameof(RegisterRequest.Password));
        problem.Errors[nameof(RegisterRequest.Password)].Should().NotBeEmpty();
        response.SessionCookieHeader().Should().BeNull();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("o'neill@example.com")]
    public async Task RegisterReportsAnUnusableEmailUnderTheEmailField(string email)
    {
        using var client = factory.CreateClient();

        using var response = await client.RegisterAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().Equal(nameof(RegisterRequest.Email));
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/sign-in")]
    public async Task MissingFieldsAreReportedByName(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync<HttpValidationProblemDetails>();
        problem.Errors.Keys.Should().BeEquivalentTo(nameof(RegisterRequest.Email), nameof(RegisterRequest.Password));
    }

    [Fact]
    public async Task SignInWithTheRightPasswordSignsInWhateverTheEmailsCase()
    {
        var email = await factory.RegisterAccountAsync();

        using var client = factory.CreateClient();
        using var response = await client.SignInAsync(email.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        body!.Email.Should().Be(email);
        (await client.GetSignedInEmailAsync()).Should().Be(email);
    }

    [Fact]
    public async Task SignInWithAWrongPasswordAnswers401AndDoesNotSignIn()
    {
        var email = await factory.RegisterAccountAsync();

        using var client = factory.CreateClient();
        using var response = await client.SignInAsync(email, password: Accounts.Password + "x");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Detail.Should().Be(AccountEndpoints.SignInFailedDetail);
        response.SessionCookieHeader().Should().BeNull();
        using var me = await client.GetCurrentUserAsync();
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownEmailGetsTheSameAnswerAsAWrongPassword()
    {
        var email = Accounts.NewEmail();
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(email);
        using (var signOut = await client.SignOutAsync())
        {
            signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var wrongPassword = await client.SignInAsync(email, password: Accounts.Password + "x");
        using var unknownEmail = await client.SignInAsync(Accounts.NewEmail());

        unknownEmail.StatusCode.Should().Be(wrongPassword.StatusCode);
        (await ComparableBodyAsync(unknownEmail)).Should().Be(await ComparableBodyAsync(wrongPassword));
    }

    [Fact]
    public async Task ALockedOutAccountGetsTheSameAnswerAsAWrongPassword()
    {
        var email = await factory.RegisterAccountAsync();

        using var client = factory.CreateClient();
        string? wrongPasswordBody = null;
        // Identity's default lockout: five failures in a row.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await client.SignInAsync(email, password: Accounts.Password + "x");
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            wrongPasswordBody = await ComparableBodyAsync(wrong);
        }

        using var rightPassword = await client.SignInAsync(email);

        rightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ComparableBodyAsync(rightPassword)).Should().Be(wrongPasswordBody);
        rightPassword.SessionCookieHeader().Should().BeNull();
    }

    [Fact]
    public async Task CurrentUserAnswers401WithProblemDetailsAndNoRedirectWhenSignedOut()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetCurrentUserAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task SignOutEndsTheSession()
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var response = await client.SignOutAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var me = await client.GetCurrentUserAsync();
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOutAnswers204WhenNobodyIsSignedIn()
    {
        using var client = factory.CreateClient();

        using var response = await client.SignOutAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // The routes Identity's ready-made endpoints (MapIdentityApi) would add,
    // with and without this API's prefix. None of them may answer.
    // POST /auth/register is this API's own, so it is checked bare only.
    private static readonly string[] IdentityApiPostAndGetRoutes =
    [
        "/register", "/login", "/refresh", "/resendConfirmationEmail",
        "/forgotPassword", "/resetPassword", "/manage/2fa", "/manage/info",
    ];

    public static TheoryData<string> IdentityApiRoutes() =>
    [
        .. IdentityApiPostAndGetRoutes,
        .. IdentityApiPostAndGetRoutes.Where(route => route != "/register").Select(route => "/auth" + route),
    ];

    [Theory]
    [MemberData(nameof(IdentityApiRoutes))]
    public async Task IdentitysOtherEndpointsAreNotMapped(string path)
    {
        using var client = factory.CreateClient();
        await client.RegisterNewAsync(Accounts.NewEmail());

        using var post = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        using var get = await client.GetAsync(path, TestContext.Current.CancellationToken);

        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/confirmEmail")]
    [InlineData("/auth/confirmEmail")]
    public async Task EmailConfirmationIsNotMapped(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path + "?userId=x&code=y", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The trace id differs on every response; everything else must match.
    private static async Task<string> ComparableBodyAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }
}
