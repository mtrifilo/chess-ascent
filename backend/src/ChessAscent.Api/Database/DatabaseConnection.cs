using Npgsql;

namespace ChessAscent.Api.Database;

/// <summary>
/// The API's connection string, read from configuration once, so EF Core and
/// the readiness check always use the same database even if a configuration
/// file is reloaded while the API runs.
/// </summary>
internal sealed class DatabaseConnection
{
    public DatabaseConnection(IConfiguration configuration)
    {
        ConnectionString = DatabaseServices.ReadConnectionString(configuration);

        // Unpooled, so each attempt really reaches the server: a pooled open
        // can hand back an idle connection without any network traffic. The
        // connect timeout is the readiness check's own.
        ReadinessConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Pooling = false,
            Timeout = (int)Math.Ceiling(DatabaseServices.ReadinessCheckTimeout.TotalSeconds),
        }.ConnectionString;
    }

    /// <summary>The connection string EF Core uses.</summary>
    public string ConnectionString { get; }

    /// <summary>The same database, as the readiness check connects to it.</summary>
    public string ReadinessConnectionString { get; }
}
