using System.Text.RegularExpressions;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Hints;
using ChessAscent.Api.Tests.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Hints;

/// <summary>
/// The Fairy-Stockfish kind as the API configures it: appsettings.json, plus
/// the settings that point it at the engines directory.
/// </summary>
public sealed partial class FairyStockfishInstallationTests
{
    private static readonly string Engines = Path.Combine(Path.GetTempPath(), "chess-ascent-engines");
    private static readonly string FairyStockfish = Path.Combine(Engines, FairyStockfishHintProvider.EngineKind);

    [Fact]
    public void TheSetUpScriptChecksTheEngineWithTheSameProgramDirectoryAndEnvironment()
    {
        var script = SetUpScripts.Read("setup-fairy-stockfish.sh");
        var kind = Kind();

        // The handshake: the program alone, with no arguments and an empty
        // environment, as the kind starts it.
        Handshake().IsMatch(script).Should().BeTrue("the script runs the new program as an ENGINE array of env -i and $CANDIDATE alone");
        kind.Arguments.Should().BeEmpty();
        kind.CopyEnvironment.Should().BeEmpty("nothing from the API's own environment reaches the engine");
        kind.SetEnvironment.Should().BeEmpty();
        kind.RandomSeedArgument.Should().BeNull("the engine's search is deterministic and takes no seed");

        // In the engine's own directory, under $ENGINES, which is where the API runs it.
        script.Should().Contain($"FAIRY=\"$(cd \"$ENGINES/{FairyStockfishHintProvider.EngineKind}\" && pwd)\"");
        script.Should().Contain("reply=\"$(cd \"$FAIRY\" && ");
        var paths = SetUpScripts.Paths(script, "FAIRY", FairyStockfish, "PROGRAM", "CANDIDATE");
        paths["PROGRAM"].Should().Be(kind.ExecutablePath);
        paths["CANDIDATE"].Should().Be(kind.ExecutablePath + ".new");
        script.Should().Contain("mv -f \"$CANDIDATE\" \"$PROGRAM\"", "the program checked is the one the kind runs");
        kind.WorkingDirectory.Should().Be(FairyStockfish);
    }

    [Fact]
    public void TheKindPassesValidation()
    {
        var options = new EnginePoolOptions();
        options.Kinds[FairyStockfishHintProvider.EngineKind] = Kind();

        var result = new EnginePoolOptionsValidator(Options.Create(new HostOptions())).Validate(null, options);

        result.Succeeded.Should().BeTrue(result.FailureMessage);
    }

    [Fact]
    public void EveryOtherSourceOverridesTheInstallationsSettings()
    {
        var kind = Kind(new Dictionary<string, string?> { ["EnginePool:Kinds:fairy_stockfish:ExecutablePath"] = "/elsewhere/fairy-stockfish" });

        kind.ExecutablePath.Should().Be("/elsewhere/fairy-stockfish");
        kind.WorkingDirectory.Should().Be(FairyStockfish);
    }

    [Fact]
    public void TheEnginesDirectorySettingMovesEveryPath()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "other-engines");

        var kind = Kind(new Dictionary<string, string?> { [EngineInstallation.EnginesDirectorySetting] = elsewhere });

        kind.ExecutablePath.Should().Be(Path.Combine(elsewhere, "fairy_stockfish", "fairy-stockfish"));
        kind.WorkingDirectory.Should().Be(Path.Combine(elsewhere, "fairy_stockfish"));
    }

    [Fact]
    public void WithoutADefaultStartUpStopsUnlessTheProgramAndDirectoryAreConfigured()
    {
        var elsewhere = Path.GetTempPath();
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["EnginePool:Kinds:fairy_stockfish:ExecutablePath"] = "/opt/fairy-stockfish" });

        var programOnly = () => configuration.AddFairyStockfishInstallation(elsewhere);

        programOnly.Should().Throw<InvalidOperationException>()
            .WithMessage("*scripts/setup-fairy-stockfish.sh installed Fairy-Stockfish*missing: EnginePool:Kinds:fairy_stockfish:WorkingDirectory.");
    }

    [GeneratedRegex(@"ENGINE=\(/usr/bin/env -i ""\$CANDIDATE""\)")]
    private static partial Regex Handshake();

    // The kind as the API configures it, with the engines directory at
    // Engines unless a later setting moves it.
    private static EngineKindOptions Kind(Dictionary<string, string?>? later = null) =>
        SetUpScripts.Kind(
            FairyStockfishHintProvider.EngineKind,
            Engines,
            configuration => configuration.AddFairyStockfishInstallation(contentRootPath: Path.Combine(Engines, "unused")),
            later);
}
