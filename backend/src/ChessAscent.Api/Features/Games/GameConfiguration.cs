using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChessAscent.Api.Features.Games;

// LEARN: An entity configuration class holds the mapping of one entity in
// code, with a fluent API, instead of in annotations on the entity: the
// counterpart of an orm.xml <entity> element, but compiled and refactorable.
// AppDbContext.OnModelCreating applies it by name; nothing scans for it.
// EF Core reads the model once, when the first context is created, and
// `dotnet ef migrations add` diffs that same model against the last
// snapshot to write a migration, so a change here reaches the database only
// through a new migration.
internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> game)
    {
        game.ToTable("Games");

        // GameStore assigns each id (a version 7 UUID, which starts with its
        // creation time, so new rows land at the end of the key's index), so
        // EF Core is told not to generate one.
        game.HasKey(g => g.Id);
        game.Property(g => g.Id).ValueGeneratedNever();

        // LEARN: A relationship is configured from one end: HasOne names the
        // entity this one points at, WithMany says the other end has many of
        // these, and HasForeignKey names the column. Together they are
        // @ManyToOne plus @JoinColumn(name = "OwnerId") on the JPA side, with
        // one difference: neither class needs a navigation property. Game
        // holds only the key, and IdentityUser (Identity's class, which this
        // code does not own) has no list of games, yet the database still
        // gets a real foreign key constraint.
        //
        // OnDelete(DeleteBehavior.Cascade) works at two levels. EF Core marks
        // as deleted any dependents the context is tracking when their
        // principal is deleted, like JPA's cascade = REMOVE; and the
        // migration writes ON DELETE CASCADE into the foreign key, like
        // Hibernate's @OnDelete(action = CASCADE), so the database removes the
        // rows EF Core never loaded, and does so for a delete issued by any
        // code. Here the games are never tracked when an account is deleted,
        // so it is the database's half that acts. (ClientCascade keeps only
        // EF Core's half.)
        //
        // Cascade is the choice because a game has no meaning without its
        // player (no other account takes part in it), and it matches
        // Identity's own tables, whose claims, logins and tokens rows cascade
        // the same way. UserManager.DeleteAsync can therefore delete an
        // account with its games and their moves; with Restrict, any future
        // account deletion would first have to delete the games itself, and
        // would fail if it forgot.
        game.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // The moves belong to the game in the same way, and Move's composite
        // key starts with GameId, so the foreign key needs no index of its own.
        game.HasMany(g => g.Moves)
            .WithOne()
            .HasForeignKey(m => m.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // LEARN: HasConversion<string>() stores an enum by its member name
        // ("Black", "Computer") instead of its number, like JPA's
        // @Enumerated(EnumType.STRING). It is one instance of a general
        // mechanism, value conversion, which is JPA's AttributeConverter:
        // any property can be given a pair of functions to and from a column
        // type. The default for an enum is its number, as with JPA's ORDINAL,
        // where reordering the members silently changes what every stored row
        // means. Names also make the table readable in psql. The cost is
        // that a rename needs a data migration (see GameValues.cs).
        game.Property(g => g.PlayerSide).HasConversion<string>();
        game.Property(g => g.OpponentKind).HasConversion<string>();
        game.Property(g => g.Status).HasConversion<string>();
        game.Property(g => g.Result).HasConversion<string>();
        game.Property(g => g.EndReason).HasConversion<string>();

        // "My games" lists one player's games, most recent activity first.
        // With this index PostgreSQL reads one player's rows in that order,
        // from a page's cursor on, without reading other players' rows. The
        // index does not hold the status, so a list also reads past the
        // player's games of the other status, all of them when the player has
        // fewer games of the listed status than a page holds; and it sorts
        // games with the same time by id (see GameStore.ListAsync). It also
        // serves the owner foreign key, which a cascading delete of an
        // account looks rows up by.
        game.HasIndex(g => new { g.OwnerId, g.UpdatedAt }).IsDescending(false, true);
    }
}
