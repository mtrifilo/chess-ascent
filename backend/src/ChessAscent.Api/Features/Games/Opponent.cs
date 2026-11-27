namespace ChessAscent.Api.Features.Games;

/// <summary>
/// Who plays against the player: nobody, or the computer at a strength
/// setting. The private constructor leaves the two factories below as the
/// only ways to make one, so a computer always has a strength and "none"
/// never does.
/// </summary>
public sealed record Opponent
{
    private Opponent(OpponentKind kind, int? strength)
    {
        Kind = kind;
        Strength = strength;
    }

    /// <summary>No opponent: the player moves both sides.</summary>
    public static Opponent None { get; } = new(OpponentKind.None, null);

    public OpponentKind Kind { get; }

    /// <summary>
    /// The computer's strength setting, such as 800; <c>null</c> when
    /// <see cref="Kind"/> is <see cref="OpponentKind.None"/>.
    /// </summary>
    public int? Strength { get; }

    /// <summary>The computer, playing at <paramref name="strength"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strength"/> is zero or negative.</exception>
    public static Opponent Computer(int strength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strength);
        return new(OpponentKind.Computer, strength);
    }
}
