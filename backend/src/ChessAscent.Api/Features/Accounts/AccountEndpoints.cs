using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ChessAscent.Api.Features.Accounts;

// Identity also ships a ready-made set of endpoints, MapIdentityApi. It is not
// used because it cannot be trimmed to this API's four: it maps nine routes
// together, including email confirmation, password reset, two-factor and
// account management, which this release does not offer. Its sign-in also
// issues bearer tokens on request and reports a locked-out account
// differently from a wrong password, and it has neither a sign-out nor a
// current-user endpoint. The handlers below call the same UserManager and
// SignInManager methods it does.
public static class AccountEndpoints
{
    /// <summary>The detail of the error a registration for an email that already has an account gets.</summary>
    public const string DuplicateEmailDetail = "An account with this email already exists.";

    /// <summary>The detail of the one error every refused sign-in gets.</summary>
    public const string SignInFailedDetail = "Email or password is incorrect.";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth");

        auth.MapPost("/register", RegisterAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/sign-in", SignInAsync)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/sign-out", SignOutAsync);

        // LEARN: RequireAuthorization attaches an authorization policy to the
        // endpoint as metadata; with no arguments, the default policy, which
        // only asks for an authenticated user. The authorization middleware
        // (see Program.cs) reads it and refuses the request before the
        // handler runs, much like .requestMatchers("/auth/me").authenticated()
        // in a Spring SecurityFilterChain, except that the rule sits on the
        // endpoint rather than in a central list of URL patterns. An endpoint
        // without it is open to anyone.
        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    /// <summary>Creates an account and signs the new player in.</summary>
    private static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        // The email doubles as the user name, so the database's unique index
        // on the normalized user name keeps one account per address even if
        // two registrations for it race past Identity's own check; the losing
        // one then fails with a 500 rather than a 409.
        var user = new IdentityUser { UserName = request.Email, Email = request.Email };

        // CreateAsync validates the password and then the email, and stores
        // only Identity's hash of the password.
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            if (RegistrationErrors.IsDuplicateEmail(created))
            {
                return TypedResults.Problem(DuplicateEmailDetail, statusCode: StatusCodes.Status409Conflict);
            }

            return RegistrationErrors.ToValidationProblem(created);
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(new CurrentUserResponse(request.Email));
    }

    /// <summary>Signs a player in with their email and password.</summary>
    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> SignInAsync(
        SignInRequest request,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        var user = await userManager.FindByNameAsync(request.Email);
        if (user?.Email is not { } email)
        {
            return SignInFailed();
        }

        // lockoutOnFailure counts this attempt towards the lockout. Every
        // outcome other than success gets the same answer, a locked-out
        // account included, so the response never shows whether an account
        // exists: only an existing account can be locked out.
        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);
        return result.Succeeded ? TypedResults.Ok(new CurrentUserResponse(email)) : SignInFailed();
    }

    /// <summary>
    /// Ends the session by telling the browser to delete the cookie. It
    /// answers 204 whether or not anyone was signed in.
    /// </summary>
    private static async Task<NoContent> SignOutAsync(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    /// <summary>The signed-in player's account.</summary>
    /// <remarks>
    /// The account is read from the database rather than from the cookie's
    /// claims, so an account that no longer exists is not reported as signed
    /// in.
    /// </remarks>
    private static async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<IdentityUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        return user?.Email is { } email
            ? TypedResults.Ok(new CurrentUserResponse(email))
            : TypedResults.Unauthorized();
    }

    private static ProblemHttpResult SignInFailed() =>
        TypedResults.Problem(SignInFailedDetail, statusCode: StatusCodes.Status401Unauthorized);
}
