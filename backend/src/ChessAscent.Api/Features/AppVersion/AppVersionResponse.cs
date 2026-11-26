using System.Reflection;

namespace ChessAscent.Api.Features.AppVersion;

// LEARN: A positional record, like a Java record: the parameters become
// read-only properties, and equality compares values. `string?` marks Commit
// as possibly null, which the compiler enforces wherever it is read.
/// <summary>The body of <c>GET /version</c>.</summary>
/// <param name="Version">The application version, such as <c>0.1.0</c>.</param>
/// <param name="Commit">
/// The git commit checked out when the API was built, or <c>null</c> when the
/// build had no git metadata to read (for example, a source archive). It does
/// not show whether the working tree had uncommitted changes.
/// </param>
public sealed record AppVersionResponse(string Version, string? Commit)
{
    /// <summary>
    /// Reads the <c>Version</c> and <c>SourceRevisionId</c> metadata that
    /// ChessAscent.Api.csproj writes into the assembly at build time.
    /// </summary>
    public static AppVersionResponse FromAssembly(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        var version = metadata.GetValueOrDefault("Version");
        if (string.IsNullOrEmpty(version))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} has no Version metadata; ChessAscent.Api.csproj writes it at build time.");
        }

        var commit = metadata.GetValueOrDefault("SourceRevisionId");
        return new AppVersionResponse(version, string.IsNullOrEmpty(commit) ? null : commit);
    }
}
