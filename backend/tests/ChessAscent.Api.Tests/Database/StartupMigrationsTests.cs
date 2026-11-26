using ChessAscent.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ChessAscent.Api.Tests.Database;

public sealed class StartupMigrationsTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task StartingTheApiAppliesEveryMigration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = StartApiAndCreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        var applied = await database.GetAppliedMigrationsAsync(cancellationToken);
        var pending = await database.GetPendingMigrationsAsync(cancellationToken);

        applied.Should().NotBeEmpty().And.Equal(database.GetMigrations());
        pending.Should().BeEmpty();
    }

    // Guards the override in ApiFactory: if a configuration file outranked it,
    // the tests would quietly run against the development database.
    [Fact]
    public async Task TheApiUsesTheTestContainerNotTheDevelopmentDatabase()
    {
        await using var scope = StartApiAndCreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        var used = new NpgsqlConnectionStringBuilder(database.GetConnectionString());
        var expected = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);

        used.Host.Should().Be(expected.Host);
        used.Port.Should().Be(expected.Port);
        used.Database.Should().Be(expected.Database);
    }

    // Reading factory.Services starts the application if it is not running
    // yet, which runs Program.cs and so its start-up migrations.
    private AsyncServiceScope StartApiAndCreateScope() => factory.Services.CreateAsyncScope();
}
