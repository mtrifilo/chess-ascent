using System.Diagnostics;
using System.Runtime.InteropServices;
using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>
/// Starts the fake engine (the ChessAscent.FakeUciEngine project) through
/// the same dotnet host that runs the tests, so nothing else need be installed.
/// </summary>
internal sealed class FakeEngine : IDisposable
{
    public static readonly string DotnetHost = FindDotnetHost();
    public static readonly string Program = Path.Combine(AppContext.BaseDirectory, "ChessAscent.FakeUciEngine.dll");

    private readonly string _directory = Directory.CreateTempSubdirectory("fake-uci-").FullName;

    public FakeEngine(params string[] behaviour)
        : this(withChild: false, behaviour)
    {
    }

    private FakeEngine(bool withChild, string[] behaviour)
    {
        string[] child = withChild ? ["--child-pid-file", ChildPidPath] : [];
        Options = new UciEngineOptions
        {
            ExecutablePath = DotnetHost,
            Arguments = ["exec", Program, "--transcript", TranscriptPath, "--pid-file", PidPath, .. child, .. behaviour],
            // Generous, because a test machine may be slow to start a .NET
            // program; the tests of timeouts set their own, shorter, waits.
            HandshakeTimeout = TimeSpan.FromSeconds(20),
            ReadyTimeout = TimeSpan.FromSeconds(10),
            SearchTimeout = TimeSpan.FromSeconds(10),
            StopTimeout = TimeSpan.FromSeconds(5),
            ExitTimeout = TimeSpan.FromSeconds(5),
        };
    }

    public UciEngineOptions Options { get; set; }

    /// <summary>A fake that starts a child process of its own; its id is in <see cref="ChildPidPath"/>.</summary>
    public static FakeEngine WithChildProcess(params string[] behaviour) => new(withChild: true, behaviour);

    public ListLogger<UciEngine> Logger { get; } = new();

    public string TranscriptPath => Path.Combine(_directory, "transcript.txt");

    public string PidPath => Path.Combine(_directory, "pid.txt");

    public string ChildPidPath => Path.Combine(_directory, "child-pid.txt");

    public Task<UciEngine> StartAsync() =>
        UciEngine.StartAsync(Options, Logger, TestContext.Current.CancellationToken);

    /// <summary>Every command the engine received, in order.</summary>
    public async Task<string[]> TranscriptAsync() =>
        File.Exists(TranscriptPath)
            ? (await File.ReadAllLinesAsync(TranscriptPath, TestContext.Current.CancellationToken))
            : [];

    /// <summary>The fake's own process id, which it writes as it starts.</summary>
    public Task<int> ReadPidAsync() => ReadIdAsync(PidPath);

    /// <summary>The id of the child that <see cref="WithChildProcess"/> starts.</summary>
    public Task<int> ReadChildPidAsync() => ReadIdAsync(ChildPidPath);

    /// <summary>
    /// Whether the process is gone. A child whose parent was killed is
    /// reaped by the operating system shortly afterwards, so this waits a
    /// little before saying no.
    /// </summary>
    public static async Task<bool> IsGoneAsync(int processId, int attempts = 50)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    // A child that outlived its engine cannot be reached by disposing the
    // engine (see UciEngine.DisposeAsync), so the test cleans it up here.
    public void Dispose()
    {
        if (File.Exists(ChildPidPath)
            && int.TryParse(File.ReadAllText(ChildPidPath), System.Globalization.CultureInfo.InvariantCulture, out var childId))
        {
            try
            {
                using var child = Process.GetProcessById(childId);
                child.Kill();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // Already gone.
            }
        }

        Directory.Delete(_directory, recursive: true);
    }

    private static async Task<int> ReadIdAsync(string path) =>
        int.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);

    // The runtime directory is <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
    private static string FindDotnetHost()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : throw new InvalidOperationException($"No dotnet host at {host}.");
    }
}
