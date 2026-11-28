using ChessAscent.Api.Opponents;
using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>Plays the computer's move in a game, checked by the chess rules before it is used.</summary>
internal static class ComputerMoves
{
    /// <summary>
    /// Asks <paramref name="opponent"/> for the next move of the game on
    /// <paramref name="board"/>, and plays it on that board if the chess
    /// rules accept it.
    /// </summary>
    /// <param name="opponent">The computer opponent.</param>
    /// <param name="board">
    /// The game, played from the standard starting position, with the
    /// computer to move and the game not over. It is changed only when the
    /// result is <see cref="MovePlayed"/>.
    /// </param>
    /// <param name="strength">The game's strength setting.</param>
    /// <param name="cancellationToken">Ends the wait for the opponent; the call then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The move as played; <see cref="OpponentUnavailable"/> when the
    /// opponent gave no move this time; or <see cref="OpponentFault"/> when
    /// what it answered means something on the server is wrong.
    /// </returns>
    // Each case of OpponentReply is mapped by hand, here and nowhere else, so
    // the union never reaches a response, and neither does the reason text of
    // OpponentFailed, which can hold what the engine wrote.
    public static async Task<ComputerMove> PlayComputerMoveAsync(
        this IOpponent opponent, IChessGame board, int strength, CancellationToken cancellationToken)
    {
        var moves = board.History.Select(move => move.Uci).ToList();
        var reply = await opponent.ChooseMoveAsync(moves, strength, cancellationToken);
        return reply switch
        {
            // The opponent has already checked its move against the same
            // rules. This check is the one the saved game depends on, so a
            // move it refuses means the opponent is broken.
            OpponentMove move => board.TryPlay(move.Uci) switch
            {
                MovePlayed played => played,
                IllegalMove => new OpponentFault("it chose a move that is not legal in the position"),
                MalformedMove => new OpponentFault("it chose something that is not a UCI move"),
                GameAlreadyOver => new OpponentFault("it was asked for a move in a finished game"),
            },
            OpponentFailed => new OpponentUnavailable(),

            // The game is rebuilt and its strength validated before the
            // opponent is asked, so each of these means the opponent and the
            // saved game disagree about what the game is.
            StrengthNotOffered notOffered => new OpponentFault($"it does not offer the strength {notOffered.Strength}"),
            InvalidMoveInHistory invalid => new OpponentFault($"it refused half-move {invalid.Index + 1} of the game"),
            GameAlreadyOver => new OpponentFault("it found the game already over"),
        };
    }
}

/// <summary>The outcome of <see cref="ComputerMoves.PlayComputerMoveAsync"/>.</summary>
internal union ComputerMove(MovePlayed, OpponentUnavailable, OpponentFault)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>
/// The opponent gave no move this time, for one of the reasons
/// <see cref="OpponentFailed"/> lists. Asking again may succeed.
/// </summary>
internal readonly record struct OpponentUnavailable;

/// <summary>
/// What the opponent answered means the server is at fault: the saved game
/// is wrong, or the opponent is.
/// </summary>
/// <param name="Reason">What went wrong, for the log. It holds nothing the opponent or the engine wrote.</param>
internal readonly record struct OpponentFault(string Reason);
