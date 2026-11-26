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
    /// Engine output with its control characters replaced, so that a log
    /// line cannot carry terminal escape sequences.
    /// </summary>
    public static string ForLog(string text) =>
        text.Any(char.IsControl)
            ? string.Create(text.Length, text, (chars, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    chars[i] = char.IsControl(source[i]) ? '?' : source[i];
                }
            })
            : text;
}
