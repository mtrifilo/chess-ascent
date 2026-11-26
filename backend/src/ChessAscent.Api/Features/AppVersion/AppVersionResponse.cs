using System.Reflection;

namespace ChessAscent.Api.Features.AppVersion;

// LEARN: A positional record, like a Java record: the parameters become
// read-only properties, and equality compares values. `string?` marks Commit
// as possibly null, which the compiler enforces wherever it is read.
/// <summary>The body of <c>GET /version</c>.</summary>
/// <param name="Version">The application version, such as <c>0.1.0</c>.</param>
/// <param name="Commit">
/// The git commit the build was made from, or <c>null</c> when the build had no
/// git metadata to read (for example, a source archive).
/// </param>
public sealed record AppVersionResponse(string Version, string? Commit)
{
    /// <summary>
    /// Reads the version from the assembly's informational version, which the
    /// .NET SDK sets to <c>Version+commit</c> when it builds inside a git
    /// repository and to plain <c>Version</c> otherwise.
    /// </summary>
    public static AppVersionResponse FromAssembly(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?? throw new InvalidOperationException(
                $"{assembly.GetName().Name} has no informational version; the SDK writes one unless GenerateAssemblyInfo is turned off.");

        return Parse(attribute.InformationalVersion);
    }

    /// <summary>
    /// Splits an informational version of the form <c>Version+commit</c>. Text
    /// after the first <c>+</c> is SemVer build metadata, which the SDK uses for
    /// the commit.
    /// </summary>
    public static AppVersionResponse Parse(string informationalVersion)
    {
        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (separator < 0)
        {
            return new AppVersionResponse(informationalVersion, null);
        }

        var commit = informationalVersion[(separator + 1)..];
        return new AppVersionResponse(
            informationalVersion[..separator],
            commit.Length == 0 ? null : commit);
    }
}
