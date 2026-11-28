using ChessAscent.Api.Database;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ChessAscent.Api.Tests;

// LEARN: WebApplicationFactory<Program> starts the real application from
// Program.cs, with its real services and middleware, on an in-memory server,
// and CreateClient() returns an HttpClient wired straight to it. It is the
// counterpart of @SpringBootTest with MockMvc or TestRestTemplate: no port is
// opened and no network is involved, but every request goes through the full
// pipeline, including the start-up migrations.
//
// LEARN: A test class receives this factory as a class fixture, and xUnit v3
// passes the shared PostgresFixture (an assembly fixture) to its constructor.
// So each test class gets its own running application, and all of them use
// one database (see AssemblyInfo.cs for what that asks of tests that write),
// the way a Spring test context points every @SpringBootTest at one
// @ServiceConnection container.
/// <summary>
/// Runs the API against a <see cref="PostgresFixture"/> rather than the
/// connection string in appsettings.Development.json.
/// </summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    /// <summary>The configuration key the API reads its connection string from.</summary>
    public static readonly string ConnectionStringKey =
        ConfigurationPath.Combine("ConnectionStrings", DatabaseServices.ConnectionStringName);

    // Test classes run in parallel, so without this their applications would
    // start, and run their start-up migrations, at the same moment against
    // one database. That is not safe on PostgreSQL once more than one
    // migration is pending: see StartupMigrations. CreateHost returns once
    // Program.cs has migrated and the host has started, so holding a lock
    // around it lets the first application migrate while the others wait,
    // and then find nothing to do. WithWebHostBuilder's factories start
    // through this method too.
    private static readonly Lock StartUpLock = new();

    /// <summary>
    /// Whether the application keeps the Maia engine kind that its
    /// configuration describes. Off unless set: on a machine where the set-up
    /// script has run, every test class's application would otherwise start
    /// a Maia process, and where it has not, readiness would fail. Without
    /// the kind, the application's IOpponent answers every request with
    /// OpponentFailed. The opponent's own tests build an engine pool around
    /// the fake engine (see MaiaOpponentTests).
    /// </summary>
    public bool KeepsMaia { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A setting given here outranks every configuration file the
        // application loads, so the API cannot reach the development database.
        builder.UseSetting(ConnectionStringKey, postgres.ConnectionString);

        if (!KeepsMaia)
        {
            // PostConfigure runs after the configuration is bound, and before
            // the options are validated and the pool reads them.
            builder.ConfigureServices(services =>
                services.PostConfigure<EnginePoolOptions>(options => options.Kinds.Remove(MaiaOpponent.EngineKind)));
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (StartUpLock)
        {
            return base.CreateHost(builder);
        }
    }
}
