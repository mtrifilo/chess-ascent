using System.Text.RegularExpressions;
using ChessAscent.Api.Engine;
using Microsoft.Extensions.Configuration;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>The engines' set-up scripts in the repository the tests were built from, and the kinds they install for.</summary>
internal static partial class SetUpScripts
{
    /// <summary>The repository's root directory, found above the directory the tests run from.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The text of a script in <c>scripts/</c>.</summary>
    public static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", name));

    /// <summary>
    /// The full paths a script assigns to the named variables, such as
    /// <c>PYTHON="$VENV/bin/python"</c>, worked out from the engine's own
    /// directory, which the script calls <paramref name="directoryVariable"/>.
    /// Each assignment may use the ones before it. An assignment of the empty
    /// string, which clears a variable once its file is dealt with, is not a
    /// path and is passed over.
    /// </summary>
    public static Dictionary<string, string> Paths(string script, string directoryVariable, string directory, params string[] names)
    {
        var paths = new Dictionary<string, string> { [directoryVariable] = directory };
        foreach (Match assignment in PathAssignment().Matches(script))
        {
            if (names.Contains(assignment.Groups["name"].Value) && assignment.Groups["value"].Length > 0)
            {
                paths[assignment.Groups["name"].Value] = Variable().Replace(assignment.Groups["value"].Value, name => paths[name.Groups["name"].Value]);
            }
        }

        return paths;
    }

    /// <summary>
    /// The kind as the API configures it: the API's appsettings.json, which
    /// the build copies next to the tests; the engines directory, and then
    /// any other settings given, as sources added after it (environment
    /// variables, say); and the installation's settings, added as
    /// Program.cs adds them.
    /// </summary>
    public static EngineKindOptions Kind(string kind, string enginesDirectory, Action<IConfigurationManager> addInstallation, Dictionary<string, string?>? later = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [EngineInstallation.EnginesDirectorySetting] = enginesDirectory });
        configuration.AddInMemoryCollection(later ?? []);
        addInstallation(configuration);

        var options = new EnginePoolOptions();
        configuration.GetSection(EnginePoolOptions.SectionName).Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        return options.Kinds[kind];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "setup-maia.sh")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No scripts/setup-maia.sh above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"^(?<name>[A-Z_]+)=""(?<value>[^""]*)""$", RegexOptions.Multiline)]
    private static partial Regex PathAssignment();

    [GeneratedRegex(@"\$(?<name>[A-Z_]+)")]
    private static partial Regex Variable();
}
