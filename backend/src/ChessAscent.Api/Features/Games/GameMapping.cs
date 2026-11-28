using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

// LEARN: The response is built field by field here, by hand, rather than by
// a mapping library such as AutoMapper, .NET's counterpart of ModelMapper.
// Copying by matching names would hide the decisions that matter in this
// mapping: the position and the legal moves come from the rebuilt board
// while the status comes from what is saved, the half-move numbers are
// worked out from each move's place in the list, and the legal moves are
// regrouped into the shape the board uses. A renamed property is a compile
// error at the line that copies it, where a reflection-based mapper finds
// the mismatch only when its configuration is validated or the mapping
// runs. And a positional record has no setters to forget: the compiler
// checks that every constructor argument is given.
internal static class GameMapping
{
    /// <summary>
    /// The response for <paramref name="game"/>, as saved, whose moves
    /// <paramref name="board"/> has played.
    /// </summary>
    /// <remarks>
    /// The status comes from the saved game rather than from the board,
    /// because a game can end in ways the board does not see, such as a
    /// resignation. A finished game therefore has no legal moves, whatever
    /// the board would allow.
    /// </remarks>
    public static GameResponse ToResponse(SavedGame game, IChessGame board)
    {
        // The positions are the ones the board wrote down as it played the
        // moves, so they cost no second replay of the game.
        var positions = board.PositionsAfterMoves;
        if (positions.Count != game.Moves.Count)
        {
            throw new InvalidOperationException(
                $"The board for game {game.Id} has played {positions.Count} moves, but the game has {game.Moves.Count}.");
        }

        var legalMoves = game.Status == GameProgress.Ongoing ? board.LegalMoves() : [];
        return new GameResponse(
            game.Id,
            game.PlayerSide,
            new OpponentResponse(game.Opponent.Kind, game.Opponent.Strength),
            board.Fen,
            board.SideToMove,
            game.Status,
            game.Result,
            game.EndReason,
            [.. game.Moves.Select((move, index) => new MoveResponse(index + 1, move.Uci, move.San, positions[index]))],
            DestinationsByOrigin(legalMoves),
            Promotions(legalMoves),
            game.HintCount,
            game.UpdatedAt);
    }

    /// <summary>The page as the list of games sends it.</summary>
    public static GameListResponse ToResponse(GameSummaryPage page) => new(
        [.. page.Games.Select(game => new GameSummaryResponse(
            game.Id,
            game.PlayerSide,
            new OpponentResponse(game.OpponentKind, game.OpponentStrength),
            game.Status,
            game.Result,
            game.EndReason,
            game.HalfMoveCount,
            game.HintCount,
            game.UpdatedAt))],
        page.Next?.Encode());

    // The four promotions of one pawn to one square are four legal moves
    // with the same two squares, so the destinations are made distinct.
    private static Dictionary<string, IReadOnlyList<string>> DestinationsByOrigin(IReadOnlyList<LegalMove> moves) =>
        moves
            .GroupBy(move => move.From)
            .ToDictionary(
                group => group.Key.ToString(),
                IReadOnlyList<string> (group) => [.. group.Select(move => move.To.ToString()).Distinct()]);

    // PromotionPiece's members are declared queen, rook, bishop, knight, so
    // sorting by value gives that order rather than the UCI letters' order.
    private static List<PromotionResponse> Promotions(IReadOnlyList<LegalMove> moves) =>
    [
        .. moves
            .Where(move => move.Promotion is not null)
            .GroupBy(move => (move.From, move.To))
            .Select(group => new PromotionResponse(
                group.Key.From.ToString(),
                group.Key.To.ToString(),
                [.. group.Select(move => move.Promotion!.Value).Order()])),
    ];
}
