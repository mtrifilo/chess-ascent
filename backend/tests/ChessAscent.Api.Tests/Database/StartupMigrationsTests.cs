using ChessAscent.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    // The migration runs before the API serves anything, so with no database
    // the process stops rather than serving liveness without a schema.
    [Fact]
    public void TheApiStopsAtStartUpWhenTheDatabaseIsUnreachable()
    {
        // Nothing listens on port 1 of the loopback address, so the
        // connection is refused at once.
        using var unreachable = factory.WithWebHostBuilder(host => host.UseSetting(
            ApiFactory.ConnectionStringKey,
            "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=5"));

        var start = () => unreachable.CreateClient();

        start.Should().Throw<NpgsqlException>();
    }

    [Fact]
    public void TheApiStopsAtStartUpWithoutAConnectionString()
    {
        using var unconfigured = factory.WithWebHostBuilder(host => host.UseSetting(ApiFactory.ConnectionStringKey, ""));

        var start = () => unconfigured.CreateClient();

        start.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(DatabaseOptions.ConnectionString));
    }
}
