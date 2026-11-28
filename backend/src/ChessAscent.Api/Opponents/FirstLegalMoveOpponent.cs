using ChessAscent.Api.Engine;
using ChessAscent.Chess;

namespace ChessAscent.Api.Opponents;

/// <summary>
/// A stand-in for the computer opponent that needs no engine: it plays the
/// first of the position's legal moves in ordinal order of their UCI text,
/// so the same game always gets the same reply.
/// </summary>
/// <remarks>
/// <para>
/// It is for the end-to-end test, which runs where no engine is installed
/// and must see the same reply every time. <see cref="OpponentServices"/>
/// selects it only when <see cref="OpponentServices.UseFirstLegalMoveSetting"/>
/// is true, and only in the Development environment.
/// </para>
/// <para>
/// It keeps the contract of <see cref="IOpponent"/>, which leaves open
/// whether a reply is always the same, with the same checks as
/// <see cref="MaiaOpponent"/> before a move is chosen: it refuses a strength
/// that is not offered, and the game is rebuilt by
/// <see cref="LegalMoveSearch.Rebuild"/>, which refuses a move list that is
/// not a legal game and a finished game. The game endpoints check the move
/// it returns with the chess rules, as they check Maia's.
/// </para>
/// </remarks>
public sealed class FirstLegalMoveOpponent(IChessRules rules) : IOpponent
{
    public Task<OpponentReply> ChooseMoveAsync(IReadOnlyList<string> moves, int strength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moves);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpponentStrengths.IsOffered(strength))
        {
            return Task.FromResult<OpponentReply>(new StrengthNotOffered(strength));
        }

        OpponentReply reply = LegalMoveSearch.Rebuild(rules, moves) switch
        {
            // LegalMoves() is ordered by UCI text, and an ongoing game has at least one.
            OngoingGame { Game: var game } => new OpponentMove(game.LegalMoves()[0].Uci),
            InvalidMoveInHistory invalid => invalid,
            GameAlreadyOver over => over,
        };
        return Task.FromResult(reply);
    }
}
