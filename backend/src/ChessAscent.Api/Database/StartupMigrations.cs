using Microsoft.EntityFrameworkCore;

namespace ChessAscent.Api.Database;

// LEARN: EF Core migrations fill the role Flyway does in a Spring Boot app:
// numbered schema changes, applied in order, with a history table in the
// database (__EFMigrationsHistory here, flyway_schema_history there) that
// records which have run. The differences: a migration is a C# class rather
// than a .sql file, and you do not write it by hand first. `dotnet ef
// migrations add <Name>` compares the model (the DbContext and its entity
// classes) with the last snapshot (Migrations/AppDbContextModelSnapshot.cs)
// and generates the class from the difference, closer to Liquibase's diff
// than to Flyway. `dotnet ef migrations has-pending-model-changes` fails when
// the model has changed without a migration to match.
//
// Spring Boot runs Flyway on start-up by itself once it is on the classpath.
// EF Core does nothing until it is asked, so the call below is the whole of
// it.
public static class StartupMigrations
{
    /// <summary>
    /// Applies every migration the database has not yet recorded, before the
    /// API serves requests. If the database cannot be reached this throws,
    /// and the process stops.
    /// </summary>
    /// <remarks>
    /// This is the only code that migrates the database. Moving migrations
    /// to a migration bundle run as its own deployment step means deleting
    /// this method and its call in Program.cs. It is also the first code to
    /// create an AppDbContext, which is what stops start-up when the
    /// connection string is missing; whatever replaces it should still create
    /// one before the API serves, or that failure moves to the first request.
    /// </remarks>
    public static async Task ApplyPendingMigrationsAsync(this WebApplication app)
    {
        // LEARN: A Scoped service such as AppDbContext can only be resolved
        // inside a scope. ASP.NET Core opens one per request; outside a request
        // you open it yourself, and `await using` disposes the scope (and the
        // context in it) at the end of the block, like try-with-resources.
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        // MigrateAsync locks the history table before reading it (on
        // PostgreSQL, LOCK TABLE ... IN ACCESS EXCLUSIVE MODE), but that lock
        // lasts only until the transaction commits, and EF Core commits after
        // each migration while working from the list of pending migrations it
        // read at the start. So with more than one migration pending, two
        // instances starting together can both apply the same later
        // migration, and one of them fails at start-up. Each migration
        // runs in its own transaction, so the failed one is rolled back, and
        // that instance finds nothing pending when it starts again. Until
        // migrations move to a separate step, start one instance at a time.
        await database.MigrateAsync();
    }
}
