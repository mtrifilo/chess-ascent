using Microsoft.Extensions.Configuration.Memory;

namespace ChessAscent.Api.Engine;

/// <summary>
/// Where a set-up script under <c>scripts/</c> installs one engine, and the
/// engine-pool settings that point the engine's kind at it.
/// </summary>
/// <remarks>
/// <para>
/// Every set-up script installs into an engines directory: the one named by
/// the <see cref="EnginesDirectorySetting"/> environment variable, or
/// <c>engines/</c> at the repository root. The API reads the same variable,
/// so the two agree; without it, the API uses the repository's
/// <c>engines/</c> only when it runs from its project directory (see
/// <see cref="EnginesDirectory"/>). Each engine has a directory of its own
/// under it, named after its kind.
/// </para>
/// <para>
/// The settings that depend on that place, such as the program and the
/// working directory, are computed here. Everything else about a kind, such
/// as its arguments, environment and timeouts, is in <c>appsettings.json</c>.
/// </para>
/// </remarks>
/// <param name="kind">The engine kind, which also names the engine's directory under the engines directory.</param>
/// <param name="engine">The engine's name, for messages.</param>
/// <param name="setUpScript">The script that installs it, from the repository root, for messages.</param>
/// <param name="settings">
/// Each setting the installation provides, by its configuration key under
/// the kind (such as <c>ExecutablePath</c> or <c>SetEnvironment:HF_HOME</c>),
/// with how its value follows from the engine's directory.
/// </param>
// LEARN: Func<string, string> is the counterpart of
// java.util.function.Function<String, String>: a delegate type, which a
// lambda such as `directory => Path.Combine(directory, "program")` converts
// to. .NET has no family of functional interfaces to choose from: Func<...>
// is a function with a result, Action<...> one without, each with up to 16
// parameters.
internal sealed class EngineInstallation(string kind, string engine, string setUpScript, IReadOnlyDictionary<string, Func<string, string>> settings)
{
    /// <summary>The environment variable that names the engines directory, for the API and the set-up scripts alike.</summary>
    public const string EnginesDirectorySetting = "CHESS_ASCENT_ENGINES_DIR";

    // In the content root only when the API runs from its source, which
    // lies at backend/src/ChessAscent.Api in the repository.
    private const string ProjectFile = "ChessAscent.Api.csproj";

    /// <summary>
    /// Adds the settings that point the kind at its installation, as
    /// defaults that every other configuration source overrides.
    /// </summary>
    /// <param name="configuration">The application's configuration, which already holds the environment variables.</param>
    /// <param name="contentRootPath">The API's content root (see <see cref="EnginesDirectory"/>).</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="EnginesDirectorySetting"/> is set to a relative path; or
    /// it is not set, there is no default, and configuration does not give
    /// every setting the installation would have provided either.
    /// </exception>
    // LEARN: Configuration is a stack of sources, like Spring's property
    // sources, and a key's value comes from the last source that has it.
    // WebApplication.CreateBuilder has already added appsettings.json, the
    // environment's appsettings file, user secrets, environment variables
    // and the command line, in that order. Add(...) would put this source on
    // top and let it override all of them; inserting it at the bottom makes
    // its values defaults instead, so that, say,
    // EnginePool__Kinds__maia__ExecutablePath still wins.
    public void AddTo(IConfigurationManager configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enginesDirectory = EnginesDirectory(configuration[EnginesDirectorySetting], contentRootPath);
        if (enginesDirectory is not null)
        {
            configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = PoolSettings(enginesDirectory) });
            return;
        }

        // Every one, or the engine would start with a setting of the API's
        // own, such as its working directory, in place of the one it needs.
        string[] keys = [.. settings.Keys.Select(Key)];
        var missing = keys.Where(key => !IsSet(configuration, key)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{EnginesDirectorySetting} is not set, and the API is not running from its project directory in a checkout of the repository, "
                + $"so there is no default. Set {EnginesDirectorySetting} to the full path of the directory {setUpScript} installed {engine} into, "
                + $"or configure all of {string.Join(", ", keys)}; missing: {string.Join(", ", missing)}.");
        }
    }

    /// <summary>The pool settings that depend on where the engine is installed, by configuration key.</summary>
    internal Dictionary<string, string?> PoolSettings(string enginesDirectory)
    {
        var directory = Path.Combine(enginesDirectory, kind);
        return settings.ToDictionary(setting => Key(setting.Key), setting => (string?)setting.Value(directory));
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
    /// The path given is relative: a set-up script would read it against
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

    // Configuration keys ignore case, but an environment variable's name
    // reaches the engine as spelt, so a variable under SetEnvironment must
    // be spelt exactly.
    private static bool IsSet(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetSection(key).Value))
        {
            return false;
        }

        var parent = ConfigurationPath.GetParentPath(key)!;
        return !ConfigurationPath.GetSectionKey(parent).Equals(nameof(EngineKindOptions.SetEnvironment), StringComparison.OrdinalIgnoreCase)
            || configuration.GetSection(parent).GetChildren().Any(child => child.Key == ConfigurationPath.GetSectionKey(key));
    }

    // A configuration key under the kind, such as EnginePool:Kinds:maia:ExecutablePath.
    private string Key(string setting) =>
        ConfigurationPath.Combine(EnginePoolOptions.SectionName, nameof(EnginePoolOptions.Kinds), kind, setting);
}
