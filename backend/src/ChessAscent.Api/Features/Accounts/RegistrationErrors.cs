using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ChessAscent.Api.Features.Accounts;

/// <summary>
/// Sorts the errors Identity reports for a refused registration into those
/// the sign-up screen tells apart: an email that already has an account, and
/// field-level problems with the email or the password.
/// </summary>
internal static class RegistrationErrors
{
    // The keys of a validation problem's errors are the C# property names of
    // the request (Email, not the JSON name email), because that is how the
    // framework's own validation (AddValidation) reports a missing field, and
    // one response must not mix two conventions.
    private const string EmailField = nameof(RegisterRequest.Email);
    private const string PasswordField = nameof(RegisterRequest.Password);

    // Identity names each refused rule with an IdentityError whose Code is
    // the name of the IdentityErrorDescriber method that built it. The email
    // is also the user name, so user-name errors are email errors here.
    private static readonly HashSet<string> DuplicateEmailCodes =
    [
        nameof(IdentityErrorDescriber.DuplicateEmail),
        nameof(IdentityErrorDescriber.DuplicateUserName),
    ];

    private static readonly HashSet<string> EmailCodes =
    [
        nameof(IdentityErrorDescriber.InvalidEmail),
        nameof(IdentityErrorDescriber.InvalidUserName),
    ];

    private static readonly HashSet<string> PasswordCodes =
    [
        nameof(IdentityErrorDescriber.PasswordTooShort),
        nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars),
        nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric),
        nameof(IdentityErrorDescriber.PasswordRequiresDigit),
        nameof(IdentityErrorDescriber.PasswordRequiresLower),
        nameof(IdentityErrorDescriber.PasswordRequiresUpper),
    ];

    /// <summary>Whether the registration was refused because the email already has an account.</summary>
    public static bool IsDuplicateEmail(IdentityResult result) =>
        result.Errors.Any(error => DuplicateEmailCodes.Contains(error.Code));

    /// <summary>A 400 validation problem listing each error under the field it concerns.</summary>
    /// <remarks>
    /// Identity reports a refused user name in its own words ("Username ...
    /// can only contain letters or digits"), which are wrong for a player who
    /// only ever typed an email. That error is reported with Identity's
    /// invalid-email text instead, once, even when Identity also found the
    /// address invalid as an email.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// An error that is about neither field, which Identity's own validators
    /// do not produce: answering with a 400 would blame the player for a fault
    /// in the server.
    /// </exception>
    public static ValidationProblem ToValidationProblem(
        IdentityResult result, string email, IdentityErrorDescriber describer) =>
        TypedResults.ValidationProblem(result.Errors
            .Select(error => error.Code == nameof(IdentityErrorDescriber.InvalidUserName)
                ? describer.InvalidEmail(email)
                : error)
            .GroupBy(error => FieldOf(error.Code), error => error.Description)
            .ToDictionary(group => group.Key, group => group.Distinct().ToArray(), StringComparer.Ordinal));

    private static string FieldOf(string code) =>
        EmailCodes.Contains(code) ? EmailField
        : PasswordCodes.Contains(code) ? PasswordField
        : throw new InvalidOperationException($"Registration failed with an unexpected Identity error: {code}.");
}
