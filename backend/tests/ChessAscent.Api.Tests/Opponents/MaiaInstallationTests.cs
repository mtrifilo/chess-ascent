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
public sealed class MaiaInstallationTests
{
    private static readonly string Engines = Path.Combine(Path.GetTempPath(), "chess-ascent-engines");
    private static readonly string Maia = Path.Combine(Engines, "maia");

    [Fact]
    public void TheEnginesDirectoryDefaultsToTheRepositoryRootSeenFromTheApiProject()
    {
        var contentRoot = Path.Combine(Engines, "repo", "backend", "src", "ChessAscent.Api");

        MaiaInstallation.EnginesDirectory(null, contentRoot).Should().Be(Path.Combine(Engines, "repo", "engines"));
        MaiaInstallation.EnginesDirectory(" ", contentRoot).Should().Be(Path.Combine(Engines, "repo", "engines"));
        MaiaInstallation.EnginesDirectory(Engines, contentRoot).Should().Be(Engines);
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

    private static EngineKindOptions Kind(ConfigurationManager configuration)
    {
        var options = new EnginePoolOptions();
        configuration.GetSection(EnginePoolOptions.SectionName).Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        return options.Kinds[MaiaOpponent.EngineKind];
    }
}
