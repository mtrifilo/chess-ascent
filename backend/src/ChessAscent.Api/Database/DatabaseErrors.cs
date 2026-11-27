using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChessAscent.Api.Database;

internal static class DatabaseErrors
{
    /// <summary>
    /// Whether a save failed because PostgreSQL refused a row that the unique
    /// index or key named <paramref name="constraintName"/> already holds.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == constraintName;
}
