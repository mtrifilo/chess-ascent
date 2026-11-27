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
/// so the two agree. Under it, <c>maia/</c> holds the Python environment
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

    /// <summary>
    /// Adds the settings that point the Maia kind at its installation, as
    /// defaults that every other configuration source overrides.
    /// </summary>
    /// <param name="configuration">The application's configuration, which already holds the environment variables.</param>
    /// <exception cref="InvalidOperationException"><see cref="EnginesDirectorySetting"/> is set to a relative path.</exception>
    /// <param name="contentRootPath">
    /// The API's content root. When <see cref="EnginesDirectorySetting"/> is
    /// not set, the engines directory is <c>engines/</c> three levels above
    /// it, which is the repository root when the API runs from its project
    /// directory, as <c>dotnet run</c> does.
    /// </param>
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
        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = PoolSettings(enginesDirectory) });
    }

    /// <summary>The engines directory, as a full path: the one given, or <c>engines/</c> three levels above the content root.</summary>
    /// <exception cref="InvalidOperationException">
    /// The path given is relative. The set-up script would read it against
    /// the shell's directory and the API against its own, which differ, so
    /// both refuse it.
    /// </exception>
    internal static string EnginesDirectory(string? given, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            return Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", "..", "engines"));
        }

        return Path.IsPathFullyQualified(given)
            ? Path.GetFullPath(given)
            : throw new InvalidOperationException($"{EnginesDirectorySetting} must be a full path, not {given}.");
    }

    /// <summary>The pool settings that depend on where Maia is installed, by configuration key.</summary>
    internal static Dictionary<string, string?> PoolSettings(string enginesDirectory)
    {
        var maia = Path.Combine(enginesDirectory, "maia");
        var kind = ConfigurationPath.Combine(EnginePoolOptions.SectionName, nameof(EnginePoolOptions.Kinds), MaiaOpponent.EngineKind);
        string Key(params string[] path) => ConfigurationPath.Combine([kind, .. path]);

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
}
