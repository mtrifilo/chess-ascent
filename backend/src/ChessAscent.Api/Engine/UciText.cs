using System.Globalization;

namespace ChessAscent.Api.Engine;

/// <summary>Splitting and checking the text that goes to and comes from an engine.</summary>
internal static class UciText
{
    /// <summary>
    /// The line's tokens. UCI allows any run of white space between tokens,
    /// and before and after them.
    /// </summary>
    public static string[] Tokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static bool HasToken(string text, string token) =>
        Tokens(text).Contains(token, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Refuses text that would end the command early: a line break would let
    /// the rest of it be read as a second command.
    /// </summary>
    public static void ThrowIfControlCharacter(string text, string parameterName)
    {
        if (text.Any(char.IsControl))
        {
            throw new ArgumentException("The text contains a line break or another control character.", parameterName);
        }
    }

    /// <summary>
    /// Engine output made safe to log: control characters (which carry
    /// terminal escape sequences), Unicode line and paragraph separators
    /// (which some viewers break lines at) and invisible format characters
    /// (such as the ones that reverse text direction) are each replaced with
    /// <c>?</c>, so that a log line cannot be forged or disguised.
    /// </summary>
    public static string ForLog(string text) =>
        text.Any(IsUnsafeForLog)
            ? string.Create(text.Length, text, (chars, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    chars[i] = IsUnsafeForLog(source[i]) ? '?' : source[i];
                }
            })
            : text;

    private static bool IsUnsafeForLog(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator;
}
