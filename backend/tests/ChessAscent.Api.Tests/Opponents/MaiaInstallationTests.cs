using System.Text.RegularExpressions;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Opponents;

/// <summary>
/// The Maia kind as the API configures it: appsettings.json, plus the
/// settings that point it at the engines directory.
/// </summary>
public sealed partial class MaiaInstallationTests
{
    private static readonly string Engines = Path.Combine(Path.GetTempPath(), "chess-ascent-engines");
    private static readonly string Maia = Path.Combine(Engines, "maia");

    [Fact]
    public void FromTheApiProjectDirectoryTheEnginesDirectoryDefaultsToTheRepositorysOwn()
    {
        var contentRoot = Path.Combine(RepositoryRoot, "backend", "src", "ChessAscent.Api");

        MaiaInstallation.EnginesDirectory(null, contentRoot).Should().Be(Path.Combine(RepositoryRoot, "engines"));
        MaiaInstallation.EnginesDirectory(" ", contentRoot).Should().Be(Path.Combine(RepositoryRoot, "engines"));
        MaiaInstallation.EnginesDirectory(Engines, contentRoot).Should().Be(Engines);
    }

    [Fact]
    public void ElsewhereThereIsNoDefault()
    {
        // A published application, which has no project file, under a
        // shared directory where the repository's own files could be faked.
        var published = Directory.CreateTempSubdirectory("published-api-");
        try
        {
            Directory.CreateDirectory(Path.Combine(published.FullName, "scripts"));
            File.WriteAllText(Path.Combine(published.FullName, "scripts", "setup-maia.sh"), "");
            var contentRoot = Directory.CreateDirectory(Path.Combine(published.FullName, "backend", "src", "ChessAscent.Api")).FullName;

            MaiaInstallation.EnginesDirectory(null, contentRoot).Should().BeNull();
        }
        finally
        {
            published.Delete(recursive: true);
        }
    }

    [Fact]
    public void WithoutADefaultStartUpStopsUnlessTheKindsProgramIsConfigured()
    {
        var elsewhere = Path.GetTempPath();

        var unset = () => new ConfigurationManager().AddMaiaInstallation(elsewhere);
        var configured = new ConfigurationManager();
        configured.AddInMemoryCollection(new Dictionary<string, string?> { ["EnginePool:Kinds:maia:ExecutablePath"] = "/opt/maia/python" });
        configured.AddMaiaInstallation(elsewhere);

        unset.Should().Throw<InvalidOperationException>().WithMessage($"{MaiaInstallation.EnginesDirectorySetting} is not set*");
        configured["EnginePool:Kinds:maia:ExecutablePath"].Should().Be("/opt/maia/python");
        configured["EnginePool:Kinds:maia:WorkingDirectory"].Should().BeNull("no default is added without an engines directory");
    }

    [Fact]
    public void ARelativeEnginesDirectoryIsRefused()
    {
        var relative = () => MaiaInstallation.EnginesDirectory("engines", Engines);

        relative.Should().Throw<InvalidOperationException>().WithMessage($"{MaiaInstallation.EnginesDirectorySetting} must be a full path*");
    }

    [Fact]
    public void TheSetUpScriptChecksTheEngineWithTheSameCommandLineEnvironmentAndPaths()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "setup-maia.sh"));
        var maia = Kind(BaseConfiguration());
        var revision = WeightsRevision().Match(script).Groups["value"].Value;

        // The handshake's command line, after `env -i "${ENGINE_ENV[@]}" "$PYTHON"`.
        var handshake = Handshake().Match(script);
        handshake.Success.Should().BeTrue("the script runs the engine as an ENGINE array of env -i, its ENGINE_ENV and $PYTHON");
        handshake.Groups["arguments"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => argument == "\"$WEIGHTS_REVISION\"" ? revision : argument)
            .Should().Equal(maia.Arguments);

        // Its environment, with the same names and values; HF_HOME is a path, checked below.
        var variables = EngineEnvironment().Match(script).Groups["variables"].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(variable => variable.Trim('"').Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        variables.Keys.Should().BeEquivalentTo(maia.SetEnvironment.Keys);
        variables.Where(variable => variable.Key != "HF_HOME").Should().BeEquivalentTo(maia.SetEnvironment.Where(variable => variable.Key != "HF_HOME"));
        variables["HF_HOME"].Should().Be("$HF_CACHE");

        // The paths it installs to, under $ENGINES/maia, are the ones the API runs from.
        script.Should().Contain("MAIA=\"$(cd \"$ENGINES/maia\" && pwd)\"");
        var paths = new Dictionary<string, string> { ["MAIA"] = Maia };
        foreach (Match assignment in PathAssignment().Matches(script))
        {
            paths[assignment.Groups["name"].Value] = Variable().Replace(assignment.Groups["value"].Value, name => paths[name.Groups["name"].Value]);
        }

        paths["PYTHON"].Should().Be(maia.ExecutablePath);
        paths["HF_CACHE"].Should().Be(maia.SetEnvironment["HF_HOME"]);
    }

    [Fact]
    public void TheKindRunsTheEnvironmentsPythonOfflineWithOnlyWhatItNeeds()
    {
        var maia = Kind(BaseConfiguration());

        maia.ExecutablePath.Should().Be(Path.Combine(Maia, "venv", "bin", "python"));
        maia.WorkingDirectory.Should().Be(Maia);
        maia.Arguments.Should().StartWith(["-I", "-m", "maia3"], "Python starts Maia's entry point itself, in isolated mode")
            .And.Contain(["--local-files-only", "--use-uci-history"]);
        maia.RandomSeedArgument.Should().Be("--seed");
        maia.CopyEnvironment.Should().BeEmpty("nothing from the API's own environment reaches Maia");
        maia.SetEnvironment.Should().Equal(new Dictionary<string, string>
        {
            ["HF_HOME"] = Path.Combine(Maia, "huggingface"),
            ["HF_HUB_OFFLINE"] = "1",
            ["OMP_NUM_THREADS"] = "1",
        });
    }

    [Fact]
    public void TheKindPassesValidation()
    {
        var options = new EnginePoolOptions();
        options.Kinds[MaiaOpponent.EngineKind] = Kind(BaseConfiguration());

        var result = new EnginePoolOptionsValidator(Options.Create(new HostOptions())).Validate(null, options);

        result.Succeeded.Should().BeTrue(result.FailureMessage);
    }

    [Fact]
    public void EveryOtherSourceOverridesTheInstallationsSettings()
    {
        var configuration = BaseConfiguration(new Dictionary<string, string?>
        {
            ["EnginePool:Kinds:maia:ExecutablePath"] = "/elsewhere/python",
        });

        var maia = Kind(configuration);

        maia.ExecutablePath.Should().Be("/elsewhere/python");
        maia.WorkingDirectory.Should().Be(Maia);
    }

    [Fact]
    public void TheEnginesDirectorySettingMovesEveryPath()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "other-engines");
        var configuration = BaseConfiguration(new Dictionary<string, string?> { [MaiaInstallation.EnginesDirectorySetting] = elsewhere });

        var maia = Kind(configuration);

        maia.ExecutablePath.Should().Be(Path.Combine(elsewhere, "maia", "venv", "bin", "python"));
        maia.WorkingDirectory.Should().Be(Path.Combine(elsewhere, "maia"));
        maia.SetEnvironment["HF_HOME"].Should().Be(Path.Combine(elsewhere, "maia", "huggingface"));
    }

    // The API's appsettings.json, which the build copies next to the tests;
    // the engines directory, and then any other settings given, as sources
    // added after it (environment variables, say); and the installation's
    // settings, added as Program.cs adds them.
    private static ConfigurationManager BaseConfiguration(Dictionary<string, string?>? later = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [MaiaInstallation.EnginesDirectorySetting] = Engines });
        configuration.AddInMemoryCollection(later ?? []);
        configuration.AddMaiaInstallation(contentRootPath: Path.Combine(Engines, "unused"));
        return configuration;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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

    [GeneratedRegex(@"^WEIGHTS_REVISION=(?<value>[0-9a-f]+)$", RegexOptions.Multiline)]
    private static partial Regex WeightsRevision();

    [GeneratedRegex(@"ENGINE=\(\s*/usr/bin/env -i ""\$\{ENGINE_ENV\[@\]\}""\s+""\$PYTHON""(?<arguments>[^)]*)\)")]
    private static partial Regex Handshake();

    [GeneratedRegex(@"ENGINE_ENV=\((?<variables>[^)]*)\)")]
    private static partial Regex EngineEnvironment();

    [GeneratedRegex(@"^(?<name>VENV|PYTHON|HF_CACHE)=""(?<value>[^""]*)""$", RegexOptions.Multiline)]
    private static partial Regex PathAssignment();

    [GeneratedRegex(@"\$(?<name>[A-Z_]+)")]
    private static partial Regex Variable();

    private static EngineKindOptions Kind(ConfigurationManager configuration)
    {
        var options = new EnginePoolOptions();
        configuration.GetSection(EnginePoolOptions.SectionName).Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        return options.Kinds[MaiaOpponent.EngineKind];
    }
}
