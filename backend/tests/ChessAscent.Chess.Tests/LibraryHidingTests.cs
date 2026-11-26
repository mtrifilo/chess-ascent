using System.Reflection;
using System.Xml.Linq;

namespace ChessAscent.Chess.Tests;

// The rules library is an implementation detail of ChessAscent.Chess, so a
// hand-written engine can replace it without changing any caller. These tests
// fail if a library type leaks into the public surface, or if another project
// starts to depend on the library directly.
public sealed class LibraryHidingTests
{
    private const string LibraryAssembly = "ChessRealms.Engine";
    private const string LibraryPackage = "ChessRealms.Engine";

    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void NoPublicSignatureMentionsALibraryType()
    {
        var exported = typeof(IChessGame).Assembly.GetExportedTypes();
        exported.Should().Contain([typeof(IChessRules), typeof(IChessGame), typeof(ChessRules)]);

        var leaks = exported
            .SelectMany(type => TypesMentionedBy(type).Select(mentioned => (type, mentioned)))
            .Where(pair => pair.mentioned.Assembly.GetName().Name == LibraryAssembly)
            .Select(pair => $"{pair.type.FullName} mentions {pair.mentioned.FullName}");

        leaks.Should().BeEmpty();
    }

    [Fact]
    public void TheLibraryIsReallyUsedSoTheCheckAboveIsNotVacuous()
    {
        typeof(IChessGame).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().Contain(LibraryAssembly);
    }

    [Fact]
    public void SurfaceWalkFindsTypesInsideGenericsArraysAndByRefParameters()
    {
        var found = TypesMentionedBy(typeof(LeakyExample)).ToList();

        found.Should().Contain([typeof(Uri), typeof(Version), typeof(TimeZoneInfo), typeof(DayOfWeek), typeof(Guid)]);
        found.Should().NotContain(typeof(System.Text.StringBuilder), "private members are not part of the surface");
    }

    [Fact]
    public void OnlyTheChessProjectReferencesTheLibrary()
    {
        var backend = BackendDirectory();
        var chessProject = ChessProjectPath();
        var centralVersions = Path.Combine(backend, "Directory.Packages.props");

        var buildFiles = Directory.EnumerateFiles(backend, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".csproj" or ".props" or ".targets")
            .Where(path => !IsBuildOutput(backend, path))
            .ToList();
        buildFiles.Should().Contain(chessProject);

        var others = buildFiles
            .Where(path => path != chessProject && path != centralVersions)
            .Where(path => File.ReadAllText(path).Contains(LibraryPackage, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(backend, path));

        others.Should().BeEmpty();
    }

    // PrivateAssets="compile" keeps the library off the compiler's reference
    // list in every project that references ChessAscent.Chess, while still
    // copying it to their output so the code runs.
    [Fact]
    public void TheLibraryDoesNotFlowToProjectsThatReferenceTheChessProject()
    {
        var chessProject = XDocument.Load(ChessProjectPath());

        var reference = chessProject.Descendants("PackageReference")
            .Single(element => (string?)element.Attribute("Include") == LibraryPackage);

        ((string?)reference.Attribute("PrivateAssets")).Should().Be("compile");
    }

    // Every type a caller outside the assembly can see in this type's
    // declaration: base type, interfaces, and the signatures of its public and
    // protected members, with generic arguments, arrays and by-ref types unwrapped.
    private static IEnumerable<Type> TypesMentionedBy(Type type)
    {
        var direct = new List<Type>();
        if (type.BaseType is { } baseType)
        {
            direct.Add(baseType);
        }

        direct.AddRange(type.GetInterfaces());

        foreach (var member in type.GetMembers(DeclaredMembers))
        {
            switch (member)
            {
                case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                    direct.Add(field.FieldType);
                    break;
                case PropertyInfo property when IsVisible(property.GetMethod) || IsVisible(property.SetMethod):
                    direct.Add(property.PropertyType);
                    break;
                case MethodBase method when IsVisible(method):
                    direct.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
                    if (method is MethodInfo withReturn)
                    {
                        direct.Add(withReturn.ReturnType);
                    }

                    break;
                case EventInfo @event when IsVisible(@event.AddMethod):
                    direct.Add(@event.EventHandlerType!);
                    break;
            }
        }

        return direct.SelectMany(Unwrap).Distinct();
    }

    private static bool IsVisible(MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.HasElementType)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type.IsGenericType
            ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type.GetGenericTypeDefinition())
            : [type];
    }

    private static string BackendDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChessAscent.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No ChessAscent.slnx above {AppContext.BaseDirectory}.");
    }

    private static string ChessProjectPath() =>
        Path.Combine(BackendDirectory(), "src", "ChessAscent.Chess", "ChessAscent.Chess.csproj");

    private static bool IsBuildOutput(string backend, string path) =>
        Path.GetRelativePath(backend, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    // A surface with one member of each kind, for checking the walk itself.
    public abstract class LeakyExample
    {
        private readonly System.Text.StringBuilder hidden = new();

#pragma warning disable CA1051 // A visible field is the point: the walk must see field types.
        public List<Uri[]> Field = [];
#pragma warning restore CA1051

        public Dictionary<int, Version>? Property { get; set; }

        protected abstract void Method(ref TimeZoneInfo zone, DayOfWeek? day);

        public event EventHandler<Guid>? Changed;

        public override string ToString() => hidden.ToString() + Changed;
    }
}
