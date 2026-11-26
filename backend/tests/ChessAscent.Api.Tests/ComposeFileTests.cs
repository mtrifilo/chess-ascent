using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ChessAscent.Api.Tests;

// docker-compose.yml, appsettings.Development.json and PostgresFixture each
// hold a copy of the same facts. Nothing can share one value between YAML,
// JSON and C#, so these tests fail when the copies drift apart.
public sealed partial class ComposeFileTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string Compose =
        File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));

    [Fact]
    public void TestsUseTheSameImageAsDevelopment()
    {
        ComposeValue(ImageLine()).Should().Be(PostgresFixture.Image);
    }

    [Fact]
    public void DevelopmentConnectionStringMatchesTheComposeDatabase()
    {
        var settings = new NpgsqlConnectionStringBuilder(DevelopmentConnectionString());

        settings.Port.Should().Be(int.Parse(ComposeValue(DefaultPortLine()), System.Globalization.CultureInfo.InvariantCulture));
        settings.Username.Should().Be(ComposeValue(EnvironmentLine("POSTGRES_USER")));
        settings.Password.Should().Be(ComposeValue(EnvironmentLine("POSTGRES_PASSWORD")));
        settings.Database.Should().Be(ComposeValue(EnvironmentLine("POSTGRES_DB")));
    }

    private static string ComposeValue(Regex line)
    {
        var match = line.Match(Compose);
        match.Success.Should().BeTrue($"docker-compose.yml should have a line matching {line}");
        return match.Groups["value"].Value;
    }

    // Read through the same JSON configuration provider the API uses, so
    // anything the API accepts (comments, trailing commas, any key case) is
    // read the same way here.
    private static string DevelopmentConnectionString()
    {
        var path = Path.Combine(RepositoryRoot, "backend", "src", "ChessAscent.Api", "appsettings.Development.json");
        var connectionString = new ConfigurationBuilder().AddJsonFile(path).Build()[ApiFactory.ConnectionStringKey];
        connectionString.Should().NotBeNullOrEmpty($"{path} should set {ApiFactory.ConnectionStringKey}");
        return connectionString!;
    }

    private static Regex EnvironmentLine(string name) =>
        new($@"^\s+{Regex.Escape(name)}:\s*(?<value>\S+)\s*$", RegexOptions.Multiline);

    [GeneratedRegex(@"^\s+image:\s*(?<value>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"\$\{CHESS_ASCENT_DB_PORT:-(?<value>\d+)\}")]
    private static partial Regex DefaultPortLine();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No docker-compose.yml above {AppContext.BaseDirectory}.");
    }
}
