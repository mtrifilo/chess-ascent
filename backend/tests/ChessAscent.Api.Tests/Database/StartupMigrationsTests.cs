using ChessAscent.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ChessAscent.Api.Tests.Database;

// Reading factory.Services starts the application if it is not running yet,
// which runs Program.cs and so its start-up migrations.
public sealed class StartupMigrationsTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task StartingTheApiAppliesEveryMigration()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        var applied = await database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        applied.Should().NotBeEmpty().And.Equal(database.GetMigrations());
    }

    // Guards the override in ApiFactory: if a configuration file outranked it,
    // the tests would quietly run against the development database.
    [Fact]
    public async Task TheApiUsesTheTestContainerNotTheDevelopmentDatabase()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        var used = new NpgsqlConnectionStringBuilder(database.GetConnectionString());
        var expected = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);

        used.Host.Should().Be(expected.Host);
        used.Port.Should().Be(expected.Port);
        used.Database.Should().Be(expected.Database);
    }
}
