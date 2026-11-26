using System.Security.Cryptography;
using Testcontainers.PostgreSql;

namespace ChessAscent.Api.Tests;

// LEARN: Testcontainers for .NET is the same project as Testcontainers for
// Java: it starts a real PostgreSQL in Docker for the tests and removes it
// afterwards, so they need Docker running. Where Java ties the container's
// life to JUnit with @Testcontainers and @Container, here an xUnit fixture
// owns it.
//
// LEARN: IAsyncLifetime gives a fixture async set-up and tear-down:
// InitializeAsync runs before the first test that uses it, and DisposeAsync
// after the last. Starting a container is I/O, so it is awaited rather than
// done in a constructor, which C# cannot make async. That is the reason this
// is not simply JUnit's @BeforeAll and @AfterAll.
/// <summary>
/// A PostgreSQL server in a Docker container. The API tests share one, as an
/// assembly fixture (see AssemblyInfo.cs); a test that needs to stop the
/// database creates its own.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// The same image as docker-compose.yml, so tests run against the server
    /// version used in development. ComposeFileTests checks that they match.
    /// </summary>
    public const string Image = "postgres:18.7";

    // Docker publishes the container's port on every network interface, so
    // the password is random for each container rather than the well-known
    // default that anyone on the same network could try.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    /// <summary>The connection string for the container, valid once it has started.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    /// <summary>Stops the server, leaving the container in place, so the database is unreachable.</summary>
    public Task StopAsync() => _container.StopAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
