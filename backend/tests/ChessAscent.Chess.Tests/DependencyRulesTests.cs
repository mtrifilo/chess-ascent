using System.Reflection;
using System.Text.Json;

namespace ChessAscent.Chess.Tests;

public sealed class DependencyRulesTests
{
    private const string ChessAssemblyName = "ChessAscent.Chess";

    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
    ];

    // Catches code in the library that uses a web or database type: the
    // compiler records an assembly reference only for assemblies the code
    // actually uses.
    [Fact]
    public void ChessLibraryCodeUsesNoWebOrDatabaseAssembly()
    {
        var referenced = Assembly.Load(ChessAssemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        referenced.Should().NotContain(name => IsForbidden(name));
    }

    // Catches a package or framework reference even before any code uses it,
    // by reading the dependency graph the SDK writes next to the test binary.
    [Fact]
    public void ChessLibraryDeclaresNoWebOrDatabaseDependency()
    {
        var depsFile = Path.Combine(AppContext.BaseDirectory, "ChessAscent.Chess.Tests.deps.json");
        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));

        var chessDependencies = deps.RootElement
            .GetProperty("targets")
            .EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Name.StartsWith(ChessAssemblyName + "/", StringComparison.Ordinal))
            .SelectMany(library => library.Value.TryGetProperty("dependencies", out var dependencies)
                ? dependencies.EnumerateObject().Select(dependency => dependency.Name)
                : []);

        chessDependencies.Should().NotContain(name => IsForbidden(name));
        RuntimeFrameworks().Should().NotContain(name => IsForbidden(name));
    }

    private static bool IsForbidden(string name) =>
        ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    // A FrameworkReference to ASP.NET Core in the library would make this test
    // program run on the ASP.NET Core shared framework, so it shows up here.
    private static List<string> RuntimeFrameworks()
    {
        var runtimeConfig = Path.Combine(AppContext.BaseDirectory, "ChessAscent.Chess.Tests.runtimeconfig.json");
        using var config = JsonDocument.Parse(File.ReadAllText(runtimeConfig));
        var options = config.RootElement.GetProperty("runtimeOptions");

        var frameworks = options.TryGetProperty("frameworks", out var many)
            ? many.EnumerateArray().ToList()
            : [options.GetProperty("framework")];

        return frameworks.Select(framework => framework.GetProperty("name").GetString() ?? string.Empty).ToList();
    }
}
