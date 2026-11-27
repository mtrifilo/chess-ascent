using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChessAscent.Api.Features.Accounts;

// Identity also ships a ready-made set of endpoints, MapIdentityApi. It is not
// used because it cannot be trimmed to this API's four: it maps nine routes
// together, including email confirmation, password reset, two-factor and
// account management, which this release does not offer. Its sign-in also
// issues bearer tokens on request and reports a locked-out account
// differently from a wrong password, and it has no sign-out. Its nearest
// thing to a current-user endpoint, GET /manage/info, sits among the
// account-management routes and cannot be kept without them. The handlers
// below call the same UserManager and SignInManager methods it does.
public static class AccountEndpoints
{
    /// <summary>The detail of the error a registration for an email that already has an account gets.</summary>
    public const string DuplicateEmailDetail = "An account with this email already exists.";

    /// <summary>The detail of the one error every refused sign-in gets.</summary>
    public const string SignInFailedDetail = "Email or password is incorrect.";

    // The unique index Identity's model puts on the normalized user name,
    // which here is the normalized email.
    private const string UniqueUserNameIndex = "UserNameIndex";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth");

        // The document learns most responses from each handler's return
        // type. The rest are declared here: 400 from validation of the body,
        // and 403 from the refusal of cross-site requests, which comes from
        // middleware (see CrossSiteRequests) and so applies to every POST.
        auth.MapPost("/register", RegisterAsync)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        auth.MapPost("/sign-in", SignInAsync)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        auth.MapPost("/sign-out", SignOutAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
        // on the normalized user name keeps one account per address.
        var user = new IdentityUser { UserName = request.Email, Email = request.Email };

        // CreateAsync validates the password and then the email, and stores
        // only Identity's hash of the password.
        IdentityResult created;
        try
        {
            created = await userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException exception) when (IsUniqueUserNameViolation(exception))
        {
            // LEARN: `catch ... when (condition)` is an exception filter: the
            // block runs only if the condition holds, and otherwise the
            // exception carries on up the stack as if this catch were not
            // there, with its stack trace untouched. Java has no counterpart;
            // there you would catch, test and rethrow.
            //
            // Two registrations for one email that arrive together can both
            // pass Identity's check for an existing account before either
            // saves, and then the index refuses the second insert. That is
            // the same duplicate as one Identity catches, so it gets the same
            // answer.
            return DuplicateEmail();
        }

        if (!created.Succeeded)
        {
            return RegistrationErrors.IsDuplicateEmail(created)
                ? DuplicateEmail()
                : RegistrationErrors.ToValidationProblem(created, request.Email, userManager.ErrorDescriber);
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
        // outcome other than success gets the same status and body, a
        // locked-out account included, because the approved sign-in screen
        // has one error for all of them. That keeps the answer from saying
        // whether an account exists or is locked. Other things still say so:
        // registering the email answers 409, and both an unknown email and a
        // locked-out account are answered sooner, without the password
        // hashing.
        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);
        return result.Succeeded ? TypedResults.Ok(new CurrentUserResponse(email)) : SignInFailed();
    }

    /// <summary>
    /// Ends the session in this browser by telling it to delete the cookie.
    /// It answers 204 whether or not anyone was signed in.
    /// </summary>
    /// <remarks>
    /// Nothing is recorded on the server, so a copy of the cookie taken
    /// before sign-out still works until it expires, and use renews it.
    /// Revoking it would mean either changing the account's security stamp,
    /// which signs out every browser the player uses, or keeping sessions on
    /// the server; neither is part of this API yet.
    /// </remarks>
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

    // The pattern checks the inner exception's type and two of its properties
    // in one expression.
    private static bool IsUniqueUserNameViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueUserNameIndex,
        };

    private static ProblemHttpResult DuplicateEmail() =>
        TypedResults.Problem(DuplicateEmailDetail, statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult SignInFailed() =>
        TypedResults.Problem(SignInFailedDetail, statusCode: StatusCodes.Status401Unauthorized);
}
