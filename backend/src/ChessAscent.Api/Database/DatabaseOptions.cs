using System.ComponentModel.DataAnnotations;

namespace ChessAscent.Api.Database;

// LEARN: The options pattern, the counterpart of a Spring Boot
// @ConfigurationProperties class. A plain class is bound to one section of
// configuration (here "Database", so the key ConnectionString is read from
// Database:ConnectionString) and injected as IOptions<DatabaseOptions>.
// Validation attributes such as [Required] work like Bean Validation on a
// @Validated properties class.
//
// Where the value comes from is up to the configuration providers that
// Program.cs describes. Locally, appsettings.Development.json holds a
// connection string for the Docker Compose database. Anywhere else, set the
// environment variable Database__ConnectionString: a double underscore
// stands for the colon between section and key, the way Spring's relaxed
// binding reads DATABASE_CONNECTIONSTRING.
/// <summary>How the API reaches its PostgreSQL database.</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// An Npgsql connection string, such as
    /// <c>Host=...;Port=...;Database=...;Username=...;Password=...</c>.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";
}
