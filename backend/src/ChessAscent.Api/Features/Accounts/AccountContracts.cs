using System.ComponentModel.DataAnnotations;

namespace ChessAscent.Api.Features.Accounts;

// LEARN: [Required] and [MaxLength] are DataAnnotations attributes, the
// counterparts of Bean Validation's @NotBlank and @Size(max = ...): on a
// string, [Required] rejects null, the empty string and a string of only
// white space. AddValidation in Program.cs is what makes the framework check
// them, much as @Valid on a Spring controller parameter does, but with no
// annotation on the endpoint: every minimal API parameter of a type that
// carries validation attributes is checked before the handler runs, and a
// failure answers 400 with a problem-details body that lists the errors under
// each property's C# name (Email, not the JSON name email). Which types and
// properties to check is worked out at compile time by a source generator,
// rather than by scanning with reflection at run time as Hibernate Validator
// does; the attributes themselves are read once at run time and cached.
//
// LEARN: In a positional record, an attribute written before a parameter
// applies to the constructor parameter only. `property:` sends it to the
// property the compiler generates instead, which is where the OpenAPI
// generator looks, so the limits also appear in the API document
// (maxLength: 256). Java records have no such target: an annotation there
// lands on every element its @Target allows.

/// <summary>The body of <c>POST /auth/register</c>.</summary>
/// <param name="Email">
/// The new account's email address, which is also what the player signs in
/// with. Compared without regard to case.
/// </param>
/// <param name="Password">The new account's password. The response names any rule it breaks.</param>
public sealed record RegisterRequest(
    [property: Required, MaxLength(AccountLimits.EmailLength)] string Email,
    [property: Required] string Password);

/// <summary>The body of <c>POST /auth/sign-in</c>.</summary>
/// <param name="Email">The account's email address, in any case.</param>
/// <param name="Password">The account's password.</param>
public sealed record SignInRequest(
    [property: Required, MaxLength(AccountLimits.EmailLength)] string Email,
    [property: Required] string Password);

public static class AccountLimits
{
    /// <summary>
    /// The longest email an account can have: the column length Identity's
    /// model gives the user name, the email and their normalized forms.
    /// Identity does not check it itself, so a longer email would reach the
    /// database and fail there.
    /// </summary>
    public const int EmailLength = 256;
}

/// <summary>The account that is signed in.</summary>
/// <param name="Email">The account's email address, as it was registered.</param>
public sealed record CurrentUserResponse(string Email);
