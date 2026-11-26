using Realms = ChessRealms.Engine;

namespace ChessAscent.Chess;

/// <summary>The standard rules of chess, as <see cref="IChessRules"/> describes them.</summary>
/// <remarks>Holds no state; one instance can serve every request.</remarks>
public sealed class ChessRules : IChessRules
{
    public IChessGame NewGame() => ChessRealmsGame.Start(new Realms.ChessGame());

    public LoadResult Load(string initialFen, IReadOnlyList<string> uciMoves)
    {
        ArgumentNullException.ThrowIfNull(initialFen);
        ArgumentNullException.ThrowIfNull(uciMoves);
        if (uciMoves.Contains(null!))
        {
            throw new ArgumentNullException(nameof(uciMoves), "The move list contains a null.");
        }

        // FEN move counters have no upper bound in the library, and a counter
        // millions of digits long makes every replayed move slow and memory
        // hungry. Capping the text at the PGN tag-value limit bounds that cost
        // and keeps every loaded game exportable.
        if (initialFen.Length > ChessRealmsGame.MaxPgnTagValueLength
            || !Realms.ChessGame.TryCreateFromFen(initialFen, out var start))
        {
            return new InvalidStartingPosition(initialFen);
        }

        // Replaying through TryPlay applies the same checks and the same
        // automatic draws as live play, so a saved game rebuilds to the state
        // it was saved in.
        var game = ChessRealmsGame.Start(start);
        for (var index = 0; index < uciMoves.Count; index++)
        {
            MoveRejection? rejection = game.TryPlay(uciMoves[index]) switch
            {
                MovePlayed => null,
                IllegalMove illegal => illegal,
                MalformedMove malformed => malformed,
                GameAlreadyOver over => over,
            };

            if (rejection is { } refused)
            {
                return new InvalidMoveInHistory(index, refused);
            }
        }

        return new GameLoaded(game);
    }
}
