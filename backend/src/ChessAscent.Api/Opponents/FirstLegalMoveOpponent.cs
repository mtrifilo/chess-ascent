using System.Diagnostics;
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
/// It keeps the contract of <see cref="IOpponent"/> as <see cref="MaiaOpponent"/>
/// does: it refuses a strength that is not offered, a move list that is not a
/// legal game and a finished game, and the game endpoints check the move it
/// returns with the chess rules, as they check Maia's.
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

        OpponentReply reply = rules.Load(rules.NewGame().Fen, moves) switch
        {
            InvalidMoveInHistory invalid => invalid,
            GameLoaded { Game.Status: not Ongoing and var status } => new GameAlreadyOver(status),
            GameLoaded { Game: var game } => new OpponentMove(game.LegalMoves().Select(move => move.Uci).Order(StringComparer.Ordinal).First()),
            InvalidStartingPosition => throw new UnreachableException("The standard starting position was refused."),
        };
        return Task.FromResult(reply);
    }
}
