using System.Globalization;

namespace ChessAscent.Api.Engine;

/// <summary>When a search ends: at a depth, or after a number of positions.</summary>
public sealed class SearchLimit
{
    private SearchLimit(string command) => Command = command;

    /// <summary>The <c>go</c> command, as it is written to the engine.</summary>
    public string Command { get; }

    /// <summary>Search this many half-moves deep (<c>go depth</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="plies"/> is less than 1.</exception>
    public static SearchLimit Depth(int plies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plies, 1);
        return new(string.Create(CultureInfo.InvariantCulture, $"go depth {plies}"));
    }

    /// <summary>Search this many positions (<c>go nodes</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodes"/> is less than 1.</exception>
    public static SearchLimit Nodes(long nodes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 1);
        return new(string.Create(CultureInfo.InvariantCulture, $"go nodes {nodes}"));
    }

    public override string ToString() => Command;
}
