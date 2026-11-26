using System.Reflection;

namespace ChessAscent.Chess.Tests;

public sealed class DependencyRulesTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "ChessAscent.Api",
    ];

    // The project file refuses web and database references at build time. This
    // checks the compiled result: the compiler records an assembly reference
    // for every assembly the library's code uses, including any that arrive
    // through another package.
    [Fact]
    public void ChessLibraryCodeUsesNoWebOrDatabaseAssembly()
    {
        var referenced = Assembly.Load("ChessAscent.Chess")
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        referenced.Should().NotContain(name =>
            ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
