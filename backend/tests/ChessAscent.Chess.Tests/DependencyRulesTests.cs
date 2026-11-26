using System.Reflection;

namespace ChessAscent.Chess.Tests;

public sealed class DependencyRulesTests
{
    // Kept in step with the ForbidWebAndDatabaseReferences target in
    // ChessAscent.Chess.csproj.
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "ChessAscent.Api",
    ];

    // The project file refuses web and database assemblies at build time. This
    // checks the compiled result as a second line: the compiler records an
    // assembly reference for every assembly the library's code uses.
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
