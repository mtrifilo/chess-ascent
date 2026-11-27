using ChessAscent.Api.Database;
using ChessAscent.Chess;
using Microsoft.EntityFrameworkCore;

namespace ChessAscent.Api.Features.Games;

/// <summary>Saves players' games and their moves, and loads them back.</summary>
/// <remarks>
/// <para>
/// Ownership is part of every call: each method takes the id of the account
/// the game belongs to, and a game that belongs to another account behaves
/// exactly as one that does not exist.
/// </para>
/// <para>
/// It records what it is given and does not check moves against the rules.
/// The caller rebuilds the game from what <see cref="LoadAsync"/> returns,
/// with <see cref="IChessRules.Load"/>, plays the moves on it through
/// <see cref="IChessGame"/>, and saves the moves and status that produced.
/// </para>
/// <para>
/// Scoped, like the <see cref="AppDbContext"/> it uses, and like it not safe
/// for concurrent calls: await each call before the next.
/// <see cref="AppendMovesAsync"/> begins and commits a transaction of its
/// own, so call it while the context has no transaction open; EF Core throws
/// if it already has one. And because SaveChanges saves everything a context
/// tracks, both writes refuse to start while the context holds changes the
/// caller has not saved, rather than save them along with the game.
/// </para>
/// </remarks>
// LEARN: TimeProvider is .NET's counterpart of java.time.Clock: code asks it
// for the time instead of calling DateTimeOffset.UtcNow, so a test can supply
// one that returns whatever time it needs.
public sealed class GameStore(AppDbContext database, TimeProvider clock)
{
    /// <summary>Creates an ongoing game with no moves.</summary>
    /// <param name="ownerId">The id of the account the game belongs to, which must exist.</param>
    /// <param name="playerSide">The side the player has.</param>
    /// <param name="opponent">Who plays the other side.</param>
    /// <param name="startingFen">The position to start from, which the chess interface has accepted.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task<SavedGame> CreateAsync(
        string ownerId, Side playerSide, Opponent opponent, string startingFen, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(opponent);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingFen);

        // LEARN: A C# enum is a named integer, not a closed set of objects as
        // in Java: (Side)7 compiles and is a value of type Side. Enum.IsDefined
        // refuses such a value here, at the boundary, before its number would
        // be stored as text.
        if (!Enum.IsDefined(playerSide))
        {
            throw new ArgumentOutOfRangeException(nameof(playerSide), playerSide, "Not a side.");
        }

        RefuseUnsavedChanges();

        var now = Now();
        var game = new Game
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = ownerId,
            PlayerSide = playerSide,
            OpponentKind = opponent.Kind,
            OpponentStrength = opponent.Strength,
            StartingFen = startingFen,
            Status = GameProgress.Ongoing,
            CreatedAt = now,
            UpdatedAt = now,
        };

        database.Games.Add(game);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // The context is the caller's for the rest of the request. Left
            // tracked, a row whose insert failed would be inserted again by
            // the caller's next SaveChanges.
            database.Entry(game).State = EntityState.Detached;
        }

        return ToSavedGame(game);
    }

    /// <summary>Loads a game with its moves.</summary>
    /// <returns>
    /// The game, or <c>null</c> when no game with <paramref name="gameId"/>
    /// belongs to <paramref name="ownerId"/>, whether because there is none
    /// or because it is someone else's.
    /// </returns>
    public async Task<SavedGame?> LoadAsync(string ownerId, Guid gameId, CancellationToken cancellationToken)
    {
        // LEARN: AsNoTracking turns off EF Core's counterpart of Hibernate's
        // session cache for this query. By default a DbContext, like a
        // Hibernate Session, keeps every entity it loads in its change
        // tracker, with a snapshot of the loaded values, so that it can
        // return the same object for the same row and write any changes at
        // the next SaveChanges (Hibernate's dirty checking at flush). A
        // read that only maps rows to a SavedGame needs none of that, so it
        // skips the snapshot and the bookkeeping, and nothing it returns can
        // be saved by accident later in the request. Hibernate's nearest
        // equivalents are a read-only query hint or a StatelessSession;
        // unlike @Transactional(readOnly = true), this is set per query.
        //
        // LEARN: Include loads the related rows in the same query, like JPQL's
        // JOIN FETCH. Without it Moves would simply stay empty: EF Core
        // does no lazy loading unless it is switched on, so there is no
        // LazyInitializationException to meet, only missing data. The OrderBy
        // inside Include sorts the collection in SQL.
        var game = await database.Games
            .AsNoTracking()
            .Include(g => g.Moves.OrderBy(m => m.HalfMoveNumber))
            .SingleOrDefaultAsync(g => g.Id == gameId && g.OwnerId == ownerId, cancellationToken);

        return game is null ? null : ToSavedGame(game);
    }

    /// <summary>
    /// Saves one or more moves at the end of a game, and the game's status
    /// after the last of them, all in one transaction.
    /// </summary>
    /// <param name="ownerId">The id of the account the game belongs to.</param>
    /// <param name="gameId">The game.</param>
    /// <param name="firstHalfMoveNumber">
    /// The half-move number of the first move: the
    /// <see cref="SavedGame.NextHalfMoveNumber"/> of the game the caller loaded
    /// and played them on.
    /// </param>
    /// <param name="moves">The moves in the order they were played, such as the player's move and the computer's reply.</param>
    /// <param name="statusAfter">The game's status after the last move, as the chess interface reported it.</param>
    /// <param name="cancellationToken">
    /// Cancels the save, which then saves nothing. Once the commit has been
    /// sent it is no longer cancelled, so a save that has happened is always
    /// reported as one.
    /// </param>
    /// <returns>
    /// <see cref="MovesAppended"/>; <see cref="GameNotFound"/> when no game
    /// with <paramref name="gameId"/> belongs to <paramref name="ownerId"/>;
    /// <see cref="GameFinished"/> when the game is over;
    /// or <see cref="HalfMoveConflict"/> when the game does not have exactly
    /// <c><paramref name="firstHalfMoveNumber"/> - 1</c> moves, or one of the
    /// new numbers is already taken. In the last three cases nothing is saved.
    /// </returns>
    public async Task<AppendResult> AppendMovesAsync(
        string ownerId,
        Guid gameId,
        int firstHalfMoveNumber,
        IReadOnlyList<PlayedMove> moves,
        GameStatus statusAfter,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstHalfMoveNumber, 1);
        ArgumentNullException.ThrowIfNull(moves);
        if (moves.Count == 0)
        {
            throw new ArgumentException("There are no moves to append.", nameof(moves));
        }

        foreach (var move in moves)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(move.Uci, nameof(moves));
            ArgumentException.ThrowIfNullOrWhiteSpace(move.San, nameof(moves));
        }

        RefuseUnsavedChanges();
        var (progress, result, endReason) = ToStoredOutcome(statusAfter);
        var now = Now();

        // LEARN: An explicit transaction. Spring's @Transactional opens and
        // commits one around a method through a proxy; EF Core has no
        // declarative transactions, so the method holds the transaction as
        // an object. `await using` disposes it at the end of the method, and
        // disposing a transaction that was never committed rolls it back, so
        // every early return below, and any exception, undoes whatever ran
        // inside it. A lone SaveChangesAsync is already atomic (it opens a
        // transaction of its own); one is needed here because the work is
        // several round trips: the update, the count and the insert.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // LEARN: ExecuteUpdateAsync sends one UPDATE ... WHERE straight to
        // the database, like a JPQL bulk update with executeUpdate(): it loads
        // nothing, bypasses the change tracker, and returns the number of rows
        // it changed. Here that number checks ownership and that the game is
        // still being played; a finished game is never reopened.
        //
        // The update also locks the game's row until the transaction ends,
        // so appends to one game run one at a time from here on: a second
        // append waits at this statement until this one commits or rolls
        // back, and the count below then sees the moves this one saved.
        var updated = await database.Games
            .Where(g => g.Id == gameId && g.OwnerId == ownerId && g.Status == GameProgress.Ongoing)
            .ExecuteUpdateAsync(
                game => game
                    .SetProperty(g => g.Status, progress)
                    .SetProperty(g => g.Result, result)
                    .SetProperty(g => g.EndReason, endReason)
                    .SetProperty(g => g.UpdatedAt, now),
                cancellationToken);
        if (updated == 0)
        {
            // Only a game the player owns is reported as finished, so this
            // says nothing about anyone else's games.
            var owned = await database.Games.AnyAsync(g => g.Id == gameId && g.OwnerId == ownerId, cancellationToken);
            return owned ? new GameFinished() : new GameNotFound();
        }

        // The new moves must follow the saved ones with no gap: a gap would
        // break the rule that the move at index i is half-move i + 1, and
        // every later append would then be refused. Counting under the row
        // lock also answers a move submitted twice before it reaches the
        // insert.
        var saved = await database.Moves.CountAsync(m => m.GameId == gameId, cancellationToken);
        if (saved != firstHalfMoveNumber - 1)
        {
            return new HalfMoveConflict();
        }

        var rows = moves
            .Select((move, index) => new Move
            {
                GameId = gameId,
                HalfMoveNumber = firstHalfMoveNumber + index,
                Uci = move.Uci,
                San = move.San,
            })
            .ToList();
        database.Moves.AddRange(rows);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception, MoveConfiguration.KeyName))
        {
            // The key of Moves is (game, half-move number), so a row already
            // holds one of these numbers even though the count matched: the
            // saved numbers have a gap that this store did not write. The
            // key still refuses the duplicate. Reloading may not help such a
            // game, since the number it would append at can be the taken one.
            //
            // Inside a transaction the caller began, SaveChanges sets a
            // savepoint and rolls back only to it on failure, so the update
            // above is still in effect here. Returning without committing
            // is what discards it.
            return new HalfMoveConflict();
        }
        finally
        {
            foreach (var row in rows)
            {
                database.Entry(row).State = EntityState.Detached;
            }
        }

        // Not cancellable: a commit cancelled after the server has applied it
        // would report a save that happened as one that did not.
        await transaction.CommitAsync(CancellationToken.None);
        return new MovesAppended(now);
    }

    private void RefuseUnsavedChanges()
    {
        if (database.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "The context holds unsaved changes, which the game store's save would save too. Save or discard them first.");
        }
    }

    // PostgreSQL keeps timestamps to the microsecond and .NET to a tenth of
    // one, so the time is cut to what the database stores. A time this class
    // returns then equals the one a later load reads back.
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static (GameProgress Progress, GameResult? Result, GameEndReason? EndReason) ToStoredOutcome(
        GameStatus boardStatus) =>
        boardStatus switch
        {
            Ongoing => (GameProgress.Ongoing, null, null),
            Checkmate checkmate => (GameProgress.Finished, WinFor(checkmate.Winner), GameEndReason.Checkmate),
            Stalemate => (GameProgress.Finished, GameResult.Draw, GameEndReason.Stalemate),
            Draw draw => (GameProgress.Finished, GameResult.Draw, ToEndReason(draw.Reason)),
        };

    // LEARN: Because an enum may hold any number of its underlying type, the
    // compiler checks a switch expression over one in two parts. Leaving out
    // a named member is warning CS8509, an error in this build, so a member
    // added to Side or DrawReason stops this file compiling until it is
    // mapped. Covering every named member but no unnamed number is CS8524.
    // A discard arm (`_ => ...`) would silence both, and with them the first
    // check, so CS8524 alone is switched off here: an unnamed number, which
    // Enum.IsDefined and the chess interface never let through, throws
    // SwitchExpressionException.
#pragma warning disable CS8524
    private static GameResult WinFor(Side winner) => winner switch
    {
        Side.White => GameResult.WhiteWins,
        Side.Black => GameResult.BlackWins,
    };

    private static GameEndReason ToEndReason(DrawReason reason) => reason switch
    {
        DrawReason.ThreefoldRepetition => GameEndReason.ThreefoldRepetition,
        DrawReason.FiftyMoveRule => GameEndReason.FiftyMoveRule,
        DrawReason.DeadPosition => GameEndReason.DeadPosition,
    };
#pragma warning restore CS8524

    private static SavedGame ToSavedGame(Game game) => new(
        game.Id,
        game.PlayerSide,
        game.OpponentKind switch
        {
            OpponentKind.None => Opponent.None,
            OpponentKind.Computer => Opponent.Computer(
                game.OpponentStrength
                ?? throw new InvalidOperationException($"Game {game.Id} has a computer opponent with no strength.")),
            _ => throw new InvalidOperationException($"Game {game.Id} has an unknown opponent kind ({game.OpponentKind})."),
        },
        game.StartingFen,
        game.Status,
        game.Result,
        game.EndReason,
        game.CreatedAt,
        game.UpdatedAt,
        [.. game.Moves.Select(move => new PlayedMove(move.Uci, move.San))]);
}
