using System.Reflection;
using System.Runtime.CompilerServices;

namespace ChessAscent.Chess.Tests;

// Games are not thread-safe, so each request makes its own. These tests check
// the other half of that rule: nothing in the library keeps a game, or any
// other mutable state, where two requests could reach it.
public sealed class SharedStateTests
{
    private const BindingFlags AllFields =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void ChessRulesHasNoFields()
    {
        typeof(ChessRules).GetFields(AllFields).Should().BeEmpty();
    }

    [Fact]
    public void NoTypeInTheChessLibraryHasAStaticField()
    {
        var staticFields = typeof(IChessGame).Assembly.GetTypes()
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type.GetFields(AllFields))
            .Where(field => field.IsStatic && !field.IsLiteral)
            .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}");

        staticFields.Should().BeEmpty();
    }

    [Fact]
    public void EveryCallReturnsANewGame()
    {
        var rules = new ChessRules();

        rules.NewGame().Should().NotBeSameAs(rules.NewGame());
        ((GameLoaded)rules.Load(Games.StartingFen, []).Value!).Game
            .Should().NotBeSameAs(((GameLoaded)rules.Load(Games.StartingFen, []).Value!).Game);
    }

    // Lambda caches and similar helpers the compiler writes are static, but
    // hold only delegates to the code, never a game.
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute))
        || (type.DeclaringType is { } outer && IsCompilerGenerated(outer));
}
