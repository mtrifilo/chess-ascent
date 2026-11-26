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

        found.Should().Contain(
        [
            typeof(Uri), typeof(Version), typeof(TimeZoneInfo), typeof(DayOfWeek), typeof(Guid),
            typeof(IFormattable), typeof(System.ComponentModel.DescriptionAttribute), typeof(SerializableAttribute),
        ]);
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

    // Directory.Packages.props must name the library, to pin its version, but
    // only as a version: a GlobalPackageReference there would add it to every
    // project. The brackets pin exactly one version.
    [Fact]
    public void CentralVersionsOnlyPinTheLibrary()
    {
        var centralVersions = XDocument.Load(Path.Combine(BackendDirectory(), "Directory.Packages.props"));

        var mentions = centralVersions.Descendants()
            .Where(element => (string?)element.Attribute("Include") == LibraryPackage)
            .Select(element => $"{element.Name.LocalName} {(string?)element.Attribute("Version")}");

        mentions.Should().Equal("PackageVersion [1.2.0]");
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
    // declaration: base type, interfaces, attributes, generic parameter
    // constraints, and the signatures and attributes of its public and
    // protected members, with generic arguments, arrays and by-ref types unwrapped.
    private static HashSet<Type> TypesMentionedBy(Type type)
    {
        var direct = new List<Type>();
        if (type.BaseType is { } baseType)
        {
            direct.Add(baseType);
        }

        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetGenericArguments());
        direct.AddRange(AttributeTypes(type));

        foreach (var member in type.GetMembers(DeclaredMembers))
        {
            if (IsVisibleMember(member))
            {
                direct.AddRange(AttributeTypes(member));
            }

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
                        direct.AddRange(withReturn.GetGenericArguments());
                    }

                    break;
                case EventInfo @event when IsVisible(@event.AddMethod):
                    direct.Add(@event.EventHandlerType!);
                    break;
            }
        }

        var found = new HashSet<Type>();
        foreach (var mentioned in direct)
        {
            Unwrap(mentioned, found);
        }

        return found;
    }

    private static bool IsVisible(MethodBase? method) =>
        method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static bool IsVisibleMember(MemberInfo member) => member switch
    {
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => IsVisible(property.GetMethod) || IsVisible(property.SetMethod),
        MethodBase method => IsVisible(method),
        EventInfo @event => IsVisible(@event.AddMethod),
        Type nested => nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem,
        _ => false,
    };

    private static IEnumerable<Type> AttributeTypes(MemberInfo member) =>
        member.GetCustomAttributesData().Select(attribute => attribute.AttributeType);

    // The set of types seen so far ends the recursion through self-referential
    // constraints such as `where T : IComparable<T>`.
    private static void Unwrap(Type type, HashSet<Type> found)
    {
        if (type.HasElementType)
        {
            Unwrap(type.GetElementType()!, found);
        }
        else if (type.IsGenericParameter)
        {
            if (found.Add(type))
            {
                foreach (var constraint in type.GetGenericParameterConstraints())
                {
                    Unwrap(constraint, found);
                }
            }
        }
        else if (type.IsGenericType)
        {
            found.Add(type.GetGenericTypeDefinition());
            foreach (var argument in type.GetGenericArguments())
            {
                Unwrap(argument, found);
            }
        }
        else
        {
            found.Add(type);
        }
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
    [Serializable]
    public abstract class LeakyExample
    {
        private readonly System.Text.StringBuilder hidden = new();

#pragma warning disable CA1051 // A visible field is the point: the walk must see field types.
        public List<Uri[]> Field = [];
#pragma warning restore CA1051

        [System.ComponentModel.Description("An attribute on a visible member.")]
        public Dictionary<int, Version>? Property { get; set; }

        public abstract T Pick<T>()
            where T : IFormattable, IComparable<T>;

        protected abstract void Method(ref TimeZoneInfo zone, DayOfWeek? day);

        public event EventHandler<Guid>? Changed;

        public override string ToString() => hidden.ToString() + Changed;
    }
}
