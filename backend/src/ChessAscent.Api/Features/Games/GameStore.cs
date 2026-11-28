using ChessAscent.Api.Database;
using ChessAscent.Chess;
using Microsoft.EntityFrameworkCore;

namespace ChessAscent.Api.Features.Games;

/// <summary>Saves players' games and their moves, loads them back, and lists a player's games.</summary>
/// <remarks>
/// <para>
/// Ownership is part of every call: each method takes the id of the account
/// the game belongs to, and a game that belongs to another account behaves
/// exactly as one that does not exist.
/// </para>
/// <para>
/// It records what it is given and does not check moves against the rules.
/// The caller rebuilds the game from what <see cref="LoadAsync"/> returns
/// with <see cref="SavedGameBoards.RebuildBoard"/>, which also refuses a
/// saved game that is broken, plays the moves on it through
/// <see cref="IChessGame"/>, and saves the moves and status that produced.
/// </para>
/// <para>
/// Scoped, like the <see cref="AppDbContext"/> it uses, and like it not safe
/// for concurrent calls: await each call before the next.
/// <see cref="AppendMovesAsync"/> and <see cref="CountHintAsync"/> begin and
/// commit a transaction of their own, so call them while the context has no
/// transaction open; EF Core throws if it already has one. And because
/// SaveChanges saves everything a context tracks, the writes that call it
/// (creating a game and appending moves) refuse to start while the context
/// holds changes the caller has not saved, rather than save them along with
/// the game.
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
    public Task<SavedGame> CreateAsync(
        string ownerId, Side playerSide, Opponent opponent, string startingFen, CancellationToken cancellationToken) =>
        CreateAsync(ownerId, playerSide, opponent, startingFen, [], new Ongoing(), cancellationToken);

    /// <summary>
    /// Creates a game with the moves already played in it, such as the
    /// computer's first move when it has White, and its status after them.
    /// The game and its moves are saved together or not at all.
    /// </summary>
    /// <param name="ownerId">The id of the account the game belongs to, which must exist.</param>
    /// <param name="playerSide">The side the player has.</param>
    /// <param name="opponent">Who plays the other side.</param>
    /// <param name="startingFen">The position to start from, which the chess interface has accepted.</param>
    /// <param name="moves">The moves played from <paramref name="startingFen"/>, oldest first; may be empty.</param>
    /// <param name="statusAfter">The game's status after the last move, as the chess interface reported it.</param>
    /// <param name="cancellationToken">Cancels the save, which then saves nothing.</param>
    public async Task<SavedGame> CreateAsync(
        string ownerId,
        Side playerSide,
        Opponent opponent,
        string startingFen,
        IReadOnlyList<PlayedMove> moves,
        GameStatus statusAfter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(opponent);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingFen);
        RefuseBlankMoves(moves);

        // LEARN: A C# enum is a named integer, not a closed set of objects as
        // in Java: (Side)7 compiles and is a value of type Side. Enum.IsDefined
        // refuses such a value here, at the boundary, before its number would
        // be stored as text.
        if (!Enum.IsDefined(playerSide))
        {
            throw new ArgumentOutOfRangeException(nameof(playerSide), playerSide, "Not a side.");
        }

        RefuseUnsavedChanges();
        var (progress, result, endReason) = ToStoredOutcome(statusAfter);

        var now = Now();
        var game = new Game
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = ownerId,
            PlayerSide = playerSide,
            OpponentKind = opponent.Kind,
            OpponentStrength = opponent.Strength,
            StartingFen = startingFen,
            Status = progress,
            Result = result,
            EndReason = endReason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var rows = MoveRows(game.Id, firstHalfMoveNumber: 1, moves);
        game.Moves.AddRange(rows);

        // LEARN: No transaction is begun here, and none is needed. JPA
        // flushes changes only inside a transaction, which Spring code gets
        // from @Transactional. EF Core's SaveChanges, called while no
        // transaction is open, begins one itself: it sends every insert it
        // has queued (the game, then its moves, ordered by the foreign key)
        // inside it and commits only if all of them succeed, so one
        // SaveChanges is all or nothing. An explicit transaction, as in
        // AppendMovesAsync, is for work that spans several round trips whose
        // results the code reads in between.
        database.Games.Add(game);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // The context is the caller's for the rest of the request. Left
            // tracked, a row whose insert failed would be inserted again by
            // the caller's next SaveChanges. Detaching a move whose insert
            // failed also takes it out of game.Moves, so the loop runs over
            // a list of its own.
            Detach(rows);
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
    /// One page of the games that belong to <paramref name="ownerId"/> and
    /// have <paramref name="status"/>, most recent activity first, in one
    /// query that reads no move rows.
    /// </summary>
    /// <param name="ownerId">The id of the account whose games are listed.</param>
    /// <param name="status">Whether to list the ongoing games or the finished ones.</param>
    /// <param name="after">
    /// Where the previous page ended, or <c>null</c> for the first page. The
    /// page holds the games that come after it in the list's order as they
    /// stand now.
    /// </param>
    /// <param name="pageSize">The most games the page holds, at least 1.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <remarks>
    /// Games with the same last activity time are ordered by id, so the order
    /// is total and the same on every request.
    /// </remarks>
    public async Task<GameSummaryPage> ListAsync(
        string ownerId, GameProgress status, GameListCursor? after, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        // LEARN: LINQ's Where, OrderBy and Select are the Stream API's
        // filter, sorted and map, and like a Stream they are lazy: this line
        // runs no query. Two differences from a Stream. A query can be
        // extended and run any number of times, where a Stream is used once;
        // each run is a new round trip, so a query enumerated twice is sent
        // twice. And the static type decides where each operator runs.
        // database.Games is an IQueryable<Game>, whose Where takes an
        // Expression<Func<Game, bool>>: the compiler hands EF Core the lambda
        // as a tree of data rather than as compiled code, and EF Core
        // translates the tree to SQL. An IEnumerable<Game>, such as a List,
        // has a Where of the same name that takes a plain Func<Game, bool>
        // and runs it in memory. Were `games` declared as IEnumerable<Game>,
        // the Where on this line would still go to SQL, but every operator
        // after it would bind to the in-memory version: the cursor condition,
        // the ordering, Take and Select would run in the application over
        // all of the player's games with this status, g.Moves.Count would
        // count a collection that was never loaded and give 0, and
        // ToListAsync, which extends only IQueryable, would not compile. The
        // nearest JPA counterpart of an IQueryable is a Criteria query, here
        // written as ordinary C#.
        var games = database.Games.Where(g => g.OwnerId == ownerId && g.Status == status);

        if (after is { } cursor)
        {
            // LEARN: Keyset paging. The next page is the rows after the
            // previous page's last row in the list's order, found by a
            // condition on the ordering columns, rather than by skipping the
            // rows already seen (Skip, SQL's OFFSET, which Spring Data's
            // Pageable uses). The database seeks to the cursor in the index
            // instead of reading and discarding every earlier row. And a game
            // that gains a move between two requests moves above the cursor,
            // where offset paging would shift every game after it down one
            // place and show the last game of one page again at the top of
            // the next. So no game is listed twice; the moved game is on no
            // later page, and is at the top when the list is read again.
            // Spring Data has the same idea as ScrollPosition.keyset() with a
            // Window result.
            //
            // In the list's order, newest first, "after the cursor" is
            // (UpdatedAt, Id) < (cursor.UpdatedAt, cursor.Id), compared
            // column by column. Both conditions below are needed for that:
            // without the first, a newer game with a smaller id would pass the
            // second. The first is also a plain comparison on UpdatedAt,
            // which PostgreSQL uses to seek in the (OwnerId, UpdatedAt DESC)
            // index; it cannot seek on the OR alone.
            //
            // LEARN: Guid has the comparison operators, as C# lets a type
            // define operators and Java does not (java.util.UUID has only
            // compareTo), and EF Core translates < on two uuids to SQL's <.
            // What EF Core translates is a fixed list of operators and
            // methods. A method it does not know, such as one of this class's
            // own, compiles here but fails when the query runs: EF Core throws
            // an InvalidOperationException saying the expression "could not be
            // translated", before any SQL is sent. As with a JPA Criteria query
            // that calls a function its provider lacks, the compiler cannot
            // catch it.
            games = games.Where(g =>
                g.UpdatedAt <= cursor.UpdatedAt
                && (g.UpdatedAt < cursor.UpdatedAt || g.Id < cursor.Id));
        }

        // LEARN: Select into a type that is not an entity is a projection,
        // like a JPQL constructor expression (SELECT new GameSummary(...)) or
        // a Spring Data DTO projection. EF Core selects only the columns the
        // lambda reads, and turns g.Moves.Count into a COUNT subquery on
        // Moves, so no move row reaches the application. A projection is
        // not tracked, so it needs no AsNoTracking. Inside Where or OrderBy
        // an untranslatable call throws, as above; in the last Select EF Core
        // instead runs it in memory on each row, after the query.
        //
        // One more row than the page holds is asked for: if it comes back,
        // there is a next page, which costs no second query to count the rows.
        //
        // LEARN: ToListAsync is the moment the query runs, as a Stream's
        // terminal operation is: EF Core translates the whole chain to one
        // SQL statement, sends it, and awaits the rows. FirstAsync, CountAsync,
        // AnyAsync and a foreach over the query run it too.
        var rows = await games
            .OrderByDescending(g => g.UpdatedAt)
            .ThenByDescending(g => g.Id)
            .Take(pageSize + 1)
            .Select(g => new GameSummary(
                g.Id,
                g.PlayerSide,
                g.OpponentKind,
                g.OpponentStrength,
                g.Status,
                g.Result,
                g.EndReason,
                g.Moves.Count,
                g.HintCount,
                g.UpdatedAt))
            .ToListAsync(cancellationToken);

        if (rows.Count <= pageSize)
        {
            return new GameSummaryPage(rows, Next: null);
        }

        rows.RemoveAt(pageSize);
        var last = rows[^1];
        return new GameSummaryPage(rows, new GameListCursor(last.UpdatedAt, last.Id));
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
        RefuseBlankMoves(moves);
        if (moves.Count == 0)
        {
            throw new ArgumentException("There are no moves to append.", nameof(moves));
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
        if (!await IsAtHalfMoveAsync(gameId, firstHalfMoveNumber, cancellationToken))
        {
            return new HalfMoveConflict();
        }

        // Read under the row lock, so it is the count the commit leaves: a
        // hint may have been counted in this position after the caller
        // loaded the game, and none can be counted before the commit.
        var hintCount = await database.Games
            .Where(g => g.Id == gameId)
            .Select(g => g.HintCount)
            .SingleAsync(cancellationToken);

        var rows = MoveRows(gameId, firstHalfMoveNumber, moves);
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
            Detach(rows);
        }

        // Not cancellable: a commit cancelled after the server has applied it
        // would report a save that happened as one that did not.
        await transaction.CommitAsync(CancellationToken.None);
        return new MovesAppended(now, progress, result, endReason, hintCount);
    }

    /// <summary>
    /// Counts a hint in the position before half-move
    /// <paramref name="halfMoveNumber"/>, unless that position has been
    /// counted already, provided the game is still in that position. Nothing
    /// else about the game changes, its update time included.
    /// </summary>
    /// <param name="ownerId">The id of the account the game belongs to.</param>
    /// <param name="gameId">The game.</param>
    /// <param name="halfMoveNumber">
    /// The half-move the hint is for: the
    /// <see cref="SavedGame.NextHalfMoveNumber"/> of the game the caller loaded
    /// and found the hint in.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the count, which then changes nothing. Once the commit has been
    /// sent it is no longer cancelled, so a count that has happened is always
    /// reported as one.
    /// </param>
    /// <returns>
    /// <see cref="HintCounted"/>, also when an earlier call counted the same
    /// position; <see cref="GameNotFound"/> when no game with
    /// <paramref name="gameId"/> belongs to <paramref name="ownerId"/>;
    /// <see cref="GameFinished"/> when the game is over; or
    /// <see cref="HalfMoveConflict"/> when the game does not have exactly
    /// <c><paramref name="halfMoveNumber"/> - 1</c> moves. In the last three
    /// cases nothing is changed.
    /// </returns>
    public async Task<HintCountResult> CountHintAsync(
        string ownerId, Guid gameId, int halfMoveNumber, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(halfMoveNumber, 1);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // LEARN: The second SetProperty is given an expression of the row,
        // not a value, so the database computes the new count: the statement
        // is UPDATE "Games" SET "HintCount" = "HintCount" + 1 ... WHERE ....
        // The JPA habit would be load-modify-save: load the entity, increment
        // the field, and let the flush write the new number. Two requests
        // that both loaded a count of 2 would then both write 3, losing one,
        // unless the entity has a version that the update checks: @Version in
        // JPA, or in EF Core a property marked as a concurrency token
        // (IsConcurrencyToken(), or the [ConcurrencyCheck] attribute), which
        // makes SaveChanges add "AND <token> = <value as loaded>" to its
        // WHERE and throw DbUpdateConcurrencyException, EF Core's
        // OptimisticLockException, when no row matches. The caller would
        // then reload and try again. Here there is nothing to retry: the
        // conditions and the increment are read and written by one statement.
        // Under READ COMMITTED, PostgreSQL's default isolation level and the
        // one this transaction uses, an UPDATE that has to wait for another
        // transaction's lock on the row checks its WHERE again, against the
        // row as that transaction left it, once the lock is free.
        //
        // Moves are only ever added at the end of a game, so a position is
        // named by the number of the half-move to be played from it, and a
        // number at or below the last one counted has been counted (see
        // Game.LastHintedHalfMoveNumber). Of two requests for the same
        // position, the second waits at this statement for the first to
        // commit, then finds the number counted and changes nothing. When
        // the update does change the row, it also leaves the row locked
        // until the transaction ends, so no move can be saved before the
        // count of moves below. When it changes nothing, it takes no lock,
        // and nothing is written on that path: the count below still sees
        // every move saved before it runs, which turns a repeat in a position
        // the game has left into a conflict.
        var counted = await database.Games
            .Where(g => g.Id == gameId
                && g.OwnerId == ownerId
                && g.Status == GameProgress.Ongoing
                && g.LastHintedHalfMoveNumber < halfMoveNumber)
            .ExecuteUpdateAsync(
                game => game
                    .SetProperty(g => g.LastHintedHalfMoveNumber, halfMoveNumber)
                    .SetProperty(g => g.HintCount, g => g.HintCount + 1),
                cancellationToken);
        if (counted == 0)
        {
            // Only a game the player owns is reported as finished, so this
            // says nothing about anyone else's games. An ongoing game that
            // the update left alone has this position counted already.
            var status = await database.Games
                .Where(g => g.Id == gameId && g.OwnerId == ownerId)
                .Select(g => (GameProgress?)g.Status)
                .SingleOrDefaultAsync(cancellationToken);
            if (status is not GameProgress.Ongoing)
            {
                return status is null ? new GameNotFound() : new GameFinished();
            }
        }

        // A move saved since the caller loaded the game means the hint is
        // for a position the game has left, so it is not counted. Disposing
        // the transaction without committing undoes the update above.
        if (!await IsAtHalfMoveAsync(gameId, halfMoveNumber, cancellationToken))
        {
            return new HalfMoveConflict();
        }

        // Not cancellable: a commit cancelled after the server has applied it
        // would report a count that happened as one that did not.
        await transaction.CommitAsync(CancellationToken.None);
        return new HintCounted();
    }

    // Whether the game has exactly the moves before halfMoveNumber, so that
    // the next move played in it is that half-move. When the caller's
    // transaction holds the game's row lock, the answer stays true until the
    // transaction ends, since no other append can save a move before then.
    private async Task<bool> IsAtHalfMoveAsync(Guid gameId, int halfMoveNumber, CancellationToken cancellationToken) =>
        await database.Moves.CountAsync(m => m.GameId == gameId, cancellationToken) == halfMoveNumber - 1;

    private static List<Move> MoveRows(Guid gameId, int firstHalfMoveNumber, IReadOnlyList<PlayedMove> moves) =>
    [
        .. moves.Select((move, index) => new Move
        {
            GameId = gameId,
            HalfMoveNumber = firstHalfMoveNumber + index,
            Uci = move.Uci,
            San = move.San,
        }),
    ];

    private void Detach(List<Move> rows)
    {
        foreach (var row in rows)
        {
            database.Entry(row).State = EntityState.Detached;
        }
    }

    private static void RefuseBlankMoves(IReadOnlyList<PlayedMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        foreach (var move in moves)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(move.Uci, nameof(moves));
            ArgumentException.ThrowIfNullOrWhiteSpace(move.San, nameof(moves));
        }
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
    // the chess interface never produces, throws SwitchExpressionException.
#pragma warning disable CS8524
    private static GameResult WinFor(Side winner) => winner switch
    {
        Side.White => GameResult.WhiteWins,
        Side.Black => GameResult.BlackWins,
    };

    // Each draw reason is stored as the end reason of the same name, which a
    // test checks for every member.
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
        game.HintCount,
        [.. game.Moves.Select(move => new PlayedMove(move.Uci, move.San))],
        game.Moves.Select((move, index) => move.HalfMoveNumber == index + 1).All(inOrder => inOrder));
}
