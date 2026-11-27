using System.Reflection;
using ChessAscent.Api.Database;
using ChessAscent.Api.Database.Migrations;
using ChessAscent.Api.Features.Games;
using ChessAscent.Chess;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ChessAscent.Api.Tests.Database;

// The shared test database is migrated from empty when the first test class
// starts the API (see StartupMigrationsTests). This checks the other path a
// real database takes: an existing schema, with data in it, brought up to
// date. It uses a database of its own in the shared server, so stepping the
// schema back and forth cannot disturb the other tests.
public sealed class MigrationUpgradeTests(PostgresFixture postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGamesMigrationUpgradesTheAccountsSchemaAndKeepsItsAccounts()
    {
        var gamesMigration = typeof(_20261127021200_AddGamesAndMoves).GetCustomAttribute<MigrationAttribute>()!.Id;
        await using var context = await NewDatabaseAsync();
        var migrations = context.Database.GetMigrations().ToList();
        var accountsSchema = migrations[migrations.IndexOf(gamesMigration) - 1];
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(accountsSchema, Cancellation);
        var account = new IdentityUser { UserName = "upgrade@example.com", Email = "upgrade@example.com" };
        context.Users.Add(account);
        await context.SaveChangesAsync(Cancellation);
        await migrator.MigrateAsync(cancellationToken: Cancellation);

        (await context.Database.GetAppliedMigrationsAsync(Cancellation)).Should().Equal(migrations);
        (await context.Users.AsNoTracking().SingleAsync(Cancellation)).Id.Should().Be(account.Id);
        var store = new GameStore(context, TimeProvider.System);
        var game = await store.CreateAsync(account.Id, Side.White, Opponent.None, "8/8/8/8/8/8/8/K6k w - - 0 1", Cancellation);
        (await store.LoadAsync(account.Id, game.Id, Cancellation)).Should().BeEquivalentTo(game);
    }

    private async Task<AppDbContext> NewDatabaseAsync()
    {
        var name = $"upgrade_{Guid.NewGuid():N}";
        await using (var server = new NpgsqlConnection(postgres.ConnectionString))
        {
            await server.OpenAsync(Cancellation);
#pragma warning disable CA2100 // The name is a GUID made above, never input; CREATE DATABASE takes no parameters.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", server);
#pragma warning restore CA2100
            await create.ExecuteNonQueryAsync(Cancellation);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
}
