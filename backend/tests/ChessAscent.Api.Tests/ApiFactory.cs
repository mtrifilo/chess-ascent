using ChessAscent.Api.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
// one database server, the way a Spring test context points every
// @SpringBootTest at one @ServiceConnection container.
/// <summary>
/// Runs the API against a <see cref="PostgresFixture"/> rather than the
/// connection string in appsettings.Development.json.
/// </summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        // A setting given here outranks every configuration file the
        // application loads, so the API cannot reach the development database.
        builder.UseSetting(
            ConfigurationPath.Combine(DatabaseOptions.SectionName, nameof(DatabaseOptions.ConnectionString)),
            postgres.ConnectionString);
}
