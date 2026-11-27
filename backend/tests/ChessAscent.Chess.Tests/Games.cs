namespace ChessAscent.Chess.Tests;

/// <summary>Shortcuts for setting up positions in tests, through the public interface only.</summary>
internal static class Games
{
    public const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // ChessRules holds no state, so one instance serves every test, including
    // tests that xUnit runs in parallel.
    public static readonly IChessRules Rules = new ChessRules();

    public static readonly string[] FoolsMate = ["f2f3", "e7e5", "g2g4", "d8h4"];

    // Both knights out and back twice: the starting position occurs for the
    // third time after the eighth half-move.
    public static readonly string[] KnightDance =
        ["g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8"];

    public static IChessGame Play(params string[] uciMoves) => Load(StartingFen, uciMoves);

    public static IChessGame Load(string fen, params string[] uciMoves) =>
        Rules.Load(fen, uciMoves) switch
        {
            GameLoaded loaded => loaded.Game,
            var other => throw new InvalidOperationException($"Expected the game to load, but got {other.Value}."),
        };

    /// <summary>Plays the moves in order, throwing with the position if one is refused.</summary>
    public static void PlayOn(IChessGame game, params string[] uciMoves)
    {
        foreach (var uci in uciMoves)
        {
            // A refused move leaves the game unchanged, so Fen is the position it was refused in.
            var result = game.TryPlay(uci);
            if (result is not MovePlayed)
            {
                throw new InvalidOperationException($"{uci} was refused in {game.Fen}: {result}.");
            }
        }
    }

    public static Square SquareNamed(string name) =>
        Square.TryParse(name, out var square) ? square : throw new ArgumentException($"Not a square: {name}.", nameof(name));
}
