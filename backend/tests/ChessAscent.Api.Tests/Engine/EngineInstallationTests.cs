using ChessAscent.Api.Engine;
using ChessAscent.Api.Opponents;
using Microsoft.Extensions.Configuration;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>How the API finds the engines directory, and what it does when it cannot.</summary>
public sealed class EngineInstallationTests
{
    private static readonly string Engines = Path.Combine(Path.GetTempPath(), "chess-ascent-engines");

    [Fact]
    public void FromTheApiProjectDirectoryTheEnginesDirectoryDefaultsToTheRepositorysOwn()
    {
        var repository = SetUpScripts.RepositoryRoot;
        var contentRoot = Path.Combine(repository, "backend", "src", "ChessAscent.Api");

        EngineInstallation.EnginesDirectory(null, contentRoot).Should().Be(Path.Combine(repository, "engines"));
        EngineInstallation.EnginesDirectory(" ", contentRoot).Should().Be(Path.Combine(repository, "engines"));
        EngineInstallation.EnginesDirectory(Engines, contentRoot).Should().Be(Engines);
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

            EngineInstallation.EnginesDirectory(null, contentRoot).Should().BeNull();
        }
        finally
        {
            published.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARelativeEnginesDirectoryIsRefused()
    {
        var relative = () => EngineInstallation.EnginesDirectory("engines", Engines);

        relative.Should().Throw<InvalidOperationException>().WithMessage($"{EngineInstallation.EnginesDirectorySetting} must be a full path*");
    }

    [Fact]
    public void WithoutADefaultStartUpStopsUnlessEverySettingOfTheInstallationIsConfigured()
    {
        // Maia's installation, which sets an environment variable as well as paths.
        var elsewhere = Path.GetTempPath();
        var program = new Dictionary<string, string?> { ["EnginePool:Kinds:maia:ExecutablePath"] = "/opt/maia/venv/bin/python" };
        var everything = new Dictionary<string, string?>(program)
        {
            ["EnginePool:Kinds:maia:WorkingDirectory"] = "/opt/maia",
            ["EnginePool:Kinds:maia:SetEnvironment:HF_HOME"] = "/opt/maia/huggingface",
        };

        var unset = () => new ConfigurationManager().AddMaiaInstallation(elsewhere);
        var programOnly = () => With(program).AddMaiaInstallation(elsewhere);
        var misspelt = () => With(new Dictionary<string, string?>(program)
        {
            ["EnginePool:Kinds:maia:WorkingDirectory"] = "/opt/maia",
            ["EnginePool:Kinds:maia:SetEnvironment:hf_home"] = "/opt/maia/huggingface",
        }).AddMaiaInstallation(elsewhere);
        var configured = With(everything);
        configured.AddMaiaInstallation(elsewhere);

        unset.Should().Throw<InvalidOperationException>()
            .WithMessage($"{EngineInstallation.EnginesDirectorySetting} is not set*scripts/setup-maia.sh installed Maia*");
        programOnly.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing: EnginePool:Kinds:maia:WorkingDirectory, EnginePool:Kinds:maia:SetEnvironment:HF_HOME.");
        misspelt.Should().Throw<InvalidOperationException>("the engine would get hf_home, which Hugging Face does not read")
            .WithMessage("*missing: EnginePool:Kinds:maia:SetEnvironment:HF_HOME.");
        configured["EnginePool:Kinds:maia:WorkingDirectory"].Should().Be("/opt/maia", "nothing is added over the configured paths");

        static ConfigurationManager With(Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(settings);
            return configuration;
        }
    }
}
