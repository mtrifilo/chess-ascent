using System.Text.RegularExpressions;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Tests.Engine;
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
    public void TheSetUpScriptChecksTheEngineWithTheSameCommandLineEnvironmentAndPaths()
    {
        var script = SetUpScripts.Read("setup-maia.sh");
        var maia = Kind();
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
        var paths = SetUpScripts.Paths(script, "MAIA", Maia, "VENV", "PYTHON", "HF_CACHE");

        paths["PYTHON"].Should().Be(maia.ExecutablePath);
        paths["HF_CACHE"].Should().Be(maia.SetEnvironment["HF_HOME"]);
    }

    [Fact]
    public void TheKindRunsTheEnvironmentsPythonOfflineWithOnlyWhatItNeeds()
    {
        var maia = Kind();

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
        options.Kinds[MaiaOpponent.EngineKind] = Kind();

        var result = new EnginePoolOptionsValidator(Options.Create(new HostOptions())).Validate(null, options);

        result.Succeeded.Should().BeTrue(result.FailureMessage);
    }

    [Fact]
    public void EveryOtherSourceOverridesTheInstallationsSettings()
    {
        var maia = Kind(new Dictionary<string, string?> { ["EnginePool:Kinds:maia:ExecutablePath"] = "/elsewhere/python" });

        maia.ExecutablePath.Should().Be("/elsewhere/python");
        maia.WorkingDirectory.Should().Be(Maia);
    }

    [Fact]
    public void TheEnginesDirectorySettingMovesEveryPath()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "other-engines");
        var maia = Kind(new Dictionary<string, string?> { [EngineInstallation.EnginesDirectorySetting] = elsewhere });

        maia.ExecutablePath.Should().Be(Path.Combine(elsewhere, "maia", "venv", "bin", "python"));
        maia.WorkingDirectory.Should().Be(Path.Combine(elsewhere, "maia"));
        maia.SetEnvironment["HF_HOME"].Should().Be(Path.Combine(elsewhere, "maia", "huggingface"));
    }

    [GeneratedRegex(@"^WEIGHTS_REVISION=(?<value>[0-9a-f]+)$", RegexOptions.Multiline)]
    private static partial Regex WeightsRevision();

    [GeneratedRegex(@"ENGINE=\(\s*/usr/bin/env -i ""\$\{ENGINE_ENV\[@\]\}""\s+""\$PYTHON""(?<arguments>[^)]*)\)")]
    private static partial Regex Handshake();

    [GeneratedRegex(@"ENGINE_ENV=\((?<variables>[^)]*)\)")]
    private static partial Regex EngineEnvironment();

    // The kind as the API configures it, with the engines directory at
    // Engines unless a later setting moves it.
    private static EngineKindOptions Kind(Dictionary<string, string?>? later = null) =>
        SetUpScripts.Kind(MaiaOpponent.EngineKind, Engines, configuration => configuration.AddMaiaInstallation(contentRootPath: Path.Combine(Engines, "unused")), later);
}
