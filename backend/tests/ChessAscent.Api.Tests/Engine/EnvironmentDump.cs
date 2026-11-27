namespace ChessAscent.Api.Tests.Engine;

/// <summary>
/// The file the fake engine writes its environment variables to
/// (<c>--env-dump</c>), in a directory of its own that only this user can
/// read on Linux and macOS, removed on <see cref="Dispose"/>.
/// </summary>
internal sealed class EnvironmentDump : IDisposable
{
    private const string Hidden = "(value not shown)";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fake-uci-env-");

    /// <summary>The arguments that make the fake write the dump.</summary>
    public string[] Arguments => ["--env-dump", FilePath];

    private string FilePath => Path.Combine(_directory.FullName, "environment.txt");

    /// <summary>
    /// The variables the fake reported, with their values shown only for the
    /// names given. A test fails exactly when a variable it did not expect
    /// reached the engine, and its message would print that variable's
    /// value, which could be a secret of whoever runs the tests.
    /// </summary>
    /// <remarks>
    /// What the fake's own runtime adds is left out. On macOS, CoreFoundation,
    /// which the .NET runtime loads, sets <c>__CF_USER_TEXT_ENCODING</c> in a
    /// process that starts without it: a fake started with an empty
    /// environment reports it, with a value other than this process's, so it
    /// is not inherited.
    /// </remarks>
    public async Task<Dictionary<string, string>> ReadAsync(params IEnumerable<string> shown)
    {
        var visible = shown.ToHashSet(StringComparer.Ordinal);
        return (await File.ReadAllLinesAsync(FilePath, TestContext.Current.CancellationToken))
            .Select(line => line.Split('=', 2))
            .Where(parts => !(OperatingSystem.IsMacOS() && parts[0] == "__CF_USER_TEXT_ENCODING"))
            .ToDictionary(parts => parts[0], parts => visible.Contains(parts[0]) ? parts[1] : Hidden);
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
