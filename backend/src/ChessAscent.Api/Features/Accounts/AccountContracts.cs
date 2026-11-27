using System.ComponentModel.DataAnnotations;

namespace ChessAscent.Api.Features.Accounts;

// LEARN: [Required] is a DataAnnotations attribute, the counterpart of Bean
// Validation's @NotBlank: on a string it rejects null, the empty string and
// a string of only white space.
// AddValidation in Program.cs is what makes the framework check it, much as
// @Valid on a Spring controller parameter does, but with no annotation on the
// endpoint: every minimal API parameter of a type that carries validation
// attributes is checked before the handler runs, and a failure answers 400
// with a problem-details body that lists the errors under each property's C#
// name (Email, not the JSON name email). The checks are
// generated at compile time rather than discovered by reflection at run time.

/// <summary>The body of <c>POST /auth/register</c>.</summary>
/// <param name="Email">
/// The new account's email address, which is also what the player signs in
/// with. Compared without regard to case.
/// </param>
/// <param name="Password">The new account's password. The response names any rule it breaks.</param>
public sealed record RegisterRequest([Required] string Email, [Required] string Password);

/// <summary>The body of <c>POST /auth/sign-in</c>.</summary>
/// <param name="Email">The account's email address, in any case.</param>
/// <param name="Password">The account's password.</param>
public sealed record SignInRequest([Required] string Email, [Required] string Password);

/// <summary>The account that is signed in.</summary>
/// <param name="Email">The account's email address, as it was registered.</param>
public sealed record CurrentUserResponse(string Email);
