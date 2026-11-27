namespace ChessAscent.Api.Opponents;

/// <summary>The strengths the computer opponent plays at.</summary>
public static class OpponentStrengths
{
    /// <summary>The strengths offered, weakest first: 600 to 1400 in steps of 200.</summary>
    public static IReadOnlyList<int> Offered { get; } = [600, 800, 1000, 1200, 1400];

    /// <summary>Whether <paramref name="strength"/> is one of <see cref="Offered"/>.</summary>
    public static bool IsOffered(int strength) => Offered.Contains(strength);
}
