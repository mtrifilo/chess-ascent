using ChessAscent.Chess;

namespace ChessAscent.Api.Features.Games;

// LEARN: An entity class in EF Core is a plain class: no @Entity, @Id or
// @Column annotations, and no base class. EF Core learns the mapping from
// conventions (a property named Id is the key; a property of a type it
// maps becomes a column) plus a configuration class (GameConfiguration),
// the role orm.xml plays for JPA when the annotations are left out. Two
// differences from a Hibernate entity. EF Core makes no proxy subclass
// unless lazy-loading or change-tracking proxies are switched on (a separate
// package, Microsoft.EntityFrameworkCore.Proxies, which needs unsealed
// classes and virtual members); this project uses neither, so the class can
// be sealed. And without those proxies there is no lazy loading: Moves holds
// what the query loaded with Include, or on a tracking context whatever
// moves that context has already loaded, and is otherwise simply empty.
//
// It is internal: only GameStore reads and writes it, and what leaves
// GameStore is a SavedGame, so the rest of the API never holds a mutable
// row.
/// <summary>A row of the Games table.</summary>
internal sealed class Game
{
    public Guid Id { get; set; }

    /// <summary>The id of the account the game belongs to (AspNetUsers.Id).</summary>
    public required string OwnerId { get; set; }

    /// <summary>The side the player has.</summary>
    public Side PlayerSide { get; set; }

    public OpponentKind OpponentKind { get; set; }

    /// <summary>The computer's strength setting; <c>null</c> when there is no opponent.</summary>
    public int? OpponentStrength { get; set; }

    /// <summary>
    /// The position the game started from, as FEN. The current position is
    /// not stored: it is rebuilt from this and the moves.
    /// </summary>
    public required string StartingFen { get; set; }

    public GameProgress Status { get; set; }

    /// <summary><c>null</c> while the game is ongoing.</summary>
    public GameResult? Result { get; set; }

    /// <summary><c>null</c> while the game is ongoing.</summary>
    public GameEndReason? EndReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the game was created or last had a move saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Move> Moves { get; } = [];
}
