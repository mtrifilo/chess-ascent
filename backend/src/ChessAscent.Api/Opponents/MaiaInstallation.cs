using ChessAscent.Api.Engine;
using Microsoft.Extensions.Configuration.Memory;

namespace ChessAscent.Api.Opponents;

/// <summary>
/// Where <c>scripts/setup-maia.sh</c> installs Maia, and the engine-pool
/// settings that point the <see cref="MaiaOpponent.EngineKind"/> kind at it.
/// </summary>
/// <remarks>
/// <para>
/// The script installs into an engines directory: the one named by the
/// <see cref="EnginesDirectorySetting"/> environment variable, or
/// <c>engines/</c> at the repository root. The API reads the same variable,
/// so the two agree; without it, the API uses the repository's
/// <c>engines/</c> only when it runs from its project directory (see
/// <see cref="AddMaiaInstallation"/>). Under it, <c>maia/</c> holds the Python environment
/// (<c>venv/</c>) and the downloaded weights (<c>huggingface/</c>).
/// </para>
/// <para>
/// The settings that depend on that place (the program, the working
/// directory and the weights' location) are computed here. Everything else
/// about the kind, such as its arguments, environment and timeouts, is in
/// <c>appsettings.json</c>.
/// </para>
/// </remarks>
public static class MaiaInstallation
{
    /// <summary>The environment variable that names the engines directory, for the API and the set-up script alike.</summary>
    public const string EnginesDirectorySetting = "CHESS_ASCENT_ENGINES_DIR";

    // In the content root only when the API runs from its source, which
    // lies at backend/src/ChessAscent.Api in the repository.
    private const string ProjectFile = "ChessAscent.Api.csproj";

    /// <summary>
    /// Adds the settings that point the Maia kind at its installation, as
    /// defaults that every other configuration source overrides.
    /// </summary>
    /// <param name="configuration">The application's configuration, which already holds the environment variables.</param>
    /// <param name="contentRootPath">
    /// The API's content root. When <see cref="EnginesDirectorySetting"/> is
    /// not set and this is the API's project directory in a checkout of the
    /// repository, as under <c>dotnet run</c>, the engines directory is
    /// <c>engines/</c> at the repository root.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="EnginesDirectorySetting"/> is set to a relative path; or
    /// it is not set, there is no default, and configuration does not give
    /// the kind's program, working directory and <c>HF_HOME</c> either.
    /// </exception>
    // LEARN: Configuration is a stack of sources, like Spring's property
    // sources, and a key's value comes from the last source that has it.
    // WebApplication.CreateBuilder has already added appsettings.json, the
    // environment's appsettings file, user secrets, environment variables
    // and the command line, in that order. Add(...) would put this source on
    // top and let it override all of them; inserting it at the bottom makes
    // its values defaults instead, so that, say,
    // EnginePool__Kinds__maia__ExecutablePath still wins.
    public static void AddMaiaInstallation(this IConfigurationManager configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enginesDirectory = EnginesDirectory(configuration[EnginesDirectorySetting], contentRootPath);
        if (enginesDirectory is not null)
        {
            configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = PoolSettings(enginesDirectory) });
        }
        else
        {
            // All three, or Maia would start in the API's directory and look
            // for its weights in the user's own Hugging Face cache.
            string[] paths = [Key(nameof(EngineKindOptions.ExecutablePath)), Key(nameof(EngineKindOptions.WorkingDirectory)), Key(nameof(EngineKindOptions.SetEnvironment), "HF_HOME")];
            var missing = paths.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{EnginesDirectorySetting} is not set, and the API is not running from its project directory in a checkout of the repository, "
                    + $"so there is no default. Set {EnginesDirectorySetting} to the full path of the directory scripts/setup-maia.sh installed Maia into, "
                    + $"or configure all of {string.Join(", ", paths)}; missing: {string.Join(", ", missing)}.");
            }
        }
    }

    /// <summary>
    /// The engines directory, as a full path: the one given; or, when the
    /// content root is the API's project directory, <c>engines/</c> at the
    /// repository root three levels above it; or null when there is neither.
    /// </summary>
    /// <remarks>
    /// The project file in the content root is what makes the default safe.
    /// A published application has none, and three levels above it could be
    /// anywhere, a shared directory such as /tmp included, where anyone could
    /// leave a program for the API to run. Whoever can write a project file
    /// into the API's own directory could change the API itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The path given is relative: the set-up script would read it against
    /// the shell's directory and the API against its own, which differ, so
    /// both refuse it.
    /// </exception>
    internal static string? EnginesDirectory(string? given, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(given))
        {
            return Path.IsPathFullyQualified(given)
                ? Path.GetFullPath(given)
                : throw new InvalidOperationException($"{EnginesDirectorySetting} must be a full path, not {given}.");
        }

        return File.Exists(Path.Combine(contentRootPath, ProjectFile))
            ? Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", "..", "engines"))
            : null;
    }

    /// <summary>The pool settings that depend on where Maia is installed, by configuration key.</summary>
    internal static Dictionary<string, string?> PoolSettings(string enginesDirectory)
    {
        var maia = Path.Combine(enginesDirectory, "maia");
        return new Dictionary<string, string?>
        {
            // The environment's own interpreter, started directly: through a
            // shell or an activation script, Python would be a grandchild of
            // the API, which the engine client cannot stop.
            [Key(nameof(EngineKindOptions.ExecutablePath))] = Path.Combine(maia, "venv", "bin", "python"),
            [Key(nameof(EngineKindOptions.WorkingDirectory))] = maia,

            // Hugging Face's cache, where the set-up script put the weights.
            // Pointing it into the installation also keeps the library away
            // from the user's own cache, token and settings.
            [Key(nameof(EngineKindOptions.SetEnvironment), "HF_HOME")] = Path.Combine(maia, "huggingface"),
        };
    }

    // A configuration key under the Maia kind, such as EnginePool:Kinds:maia:ExecutablePath.
    private static string Key(params string[] path) =>
        ConfigurationPath.Combine([EnginePoolOptions.SectionName, nameof(EnginePoolOptions.Kinds), MaiaOpponent.EngineKind, .. path]);
}
