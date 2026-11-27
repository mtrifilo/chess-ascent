using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChessAscent.Api.Features.Games;

internal sealed class MoveConfiguration : IEntityTypeConfiguration<Move>
{
    /// <summary>
    /// The name of the Moves table's primary key, which is what refuses a
    /// second move with the same half-move number in a game.
    /// </summary>
    public const string KeyName = "PK_Moves";

    public void Configure(EntityTypeBuilder<Move> move)
    {
        move.ToTable("Moves");

        // LEARN: A composite key is one line here; JPA needs an @IdClass or
        // an @EmbeddedId class for it. A primary key is enforced by a unique
        // index, so this key is also the uniqueness rule on (game, half-move
        // number): the database refuses a second row for the same half-move
        // of a game, whatever code writes it. GameStore.AppendMovesAsync
        // checks the count of saved moves first, under the game's row lock,
        // and this key is the guarantee behind that check. A uniqueness rule
        // that is not the key is declared with HasIndex(...).IsUnique(), the
        // counterpart of JPA's @Table(uniqueConstraints = ...). A separate
        // surrogate id would add a column and a second index and enforce
        // nothing more.
        move.HasKey(m => new { m.GameId, m.HalfMoveNumber }).HasName(KeyName);
    }
}
