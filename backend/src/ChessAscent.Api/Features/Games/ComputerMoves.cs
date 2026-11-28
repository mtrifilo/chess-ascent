using ChessAscent.Api.Opponents;
using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

/// <summary>Plays the computer's move in a game, checked by the chess rules before it is used.</summary>
internal static class ComputerMoves
{
    /// <summary>
    /// Plays the computer's move on <paramref name="board"/> when it is the
    /// computer's turn: the game has a computer opponent, is not over, and
    /// the side to move is not the player's.
    /// </summary>
    /// <param name="computer">The computer opponent.</param>
    /// <param name="opponent">Who plays against the player in this game.</param>
    /// <param name="playerSide">The side the player has.</param>
    /// <param name="board">
    /// The game, played from the standard starting position. It is changed
    /// only when the result is <see cref="MovePlayed"/>.
    /// </param>
    /// <param name="cancellationToken">Ends the wait for the opponent; the call then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The computer's move as played; <see cref="NotComputersTurn"/>;
    /// <see cref="OpponentUnavailable"/> when the opponent gave no move this
    /// time; or <see cref="OpponentFault"/> when what it answered means
    /// something on the server is wrong.
    /// </returns>
    public static Task<ComputerTurn> PlayComputerTurnAsync(
        this IOpponent computer, Opponent opponent, Side playerSide, IChessGame board, CancellationToken cancellationToken) =>
        opponent is { Kind: OpponentKind.Computer, Strength: int strength }
            && board.Status is Ongoing
            && board.SideToMove != playerSide
            ? computer.PlayComputerMoveAsync(board, strength, cancellationToken)
            : Task.FromResult<ComputerTurn>(new NotComputersTurn());

    // Asks the opponent for the next move and plays it on the board if the
    // chess rules accept it. Each case of OpponentReply is mapped by hand,
    // here and nowhere else, so the union never reaches a response, and
    // neither does the reason text of OpponentFailed, which can hold what
    // the engine wrote.
    private static async Task<ComputerTurn> PlayComputerMoveAsync(
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

            // The game is rebuilt by the same chess rules before the opponent
            // is asked, and its strength was checked when the game was
            // created. So each of these means the saved game is wrong, such
            // as a strength no longer offered, or the opponent is.
            StrengthNotOffered notOffered => new OpponentFault($"it does not offer the strength {notOffered.Strength}"),
            InvalidMoveInHistory invalid => new OpponentFault($"it refused half-move {invalid.Index + 1} of the game"),
            GameAlreadyOver => new OpponentFault("it found the game already over"),
        };
    }
}

/// <summary>The outcome of <see cref="ComputerMoves.PlayComputerTurnAsync"/>.</summary>
internal union ComputerTurn(MovePlayed, NotComputersTurn, OpponentUnavailable, OpponentFault)
{
    public override string ToString() => Value?.ToString() ?? "(no case)";
}

/// <summary>It is not the computer's turn, so it was not asked: nobody plays the other side, the game is over, or it is the player's move.</summary>
internal readonly record struct NotComputersTurn;

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
