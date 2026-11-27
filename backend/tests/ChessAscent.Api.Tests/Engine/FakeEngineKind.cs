using System.Diagnostics;
using System.Globalization;
using ChessAscent.Api.Engine;
using Microsoft.Extensions.Configuration;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>
/// A kind of engine, for the pool, that runs the fake engine. Each process
/// it starts leaves a file named by its process id in a directory of its own,
/// so a test can find every process the pool started.
/// </summary>
internal sealed class FakeEngineKind : IDisposable
{
    private readonly string _pidDirectory = Directory.CreateTempSubdirectory("fake-kind-").FullName;
    private readonly string[] _behaviour;

    public FakeEngineKind(params string[] behaviour) => _behaviour = behaviour;

    /// <summary>
    /// Options that start the fake with its behaviour. The waits are short
    /// enough that stopping fits well within the host's shutdown timeout,
    /// and generous enough for a slow test machine to start a .NET program.
    /// </summary>
    public EngineKindOptions Options(
        int processes = 1,
        TimeSpan? checkOutTimeout = null,
        int startAttempts = 3,
        TimeSpan? restartDelay = null,
        TimeSpan? readyTimeout = null,
        TimeSpan? exitTimeout = null,
        string? executablePath = null)
    {
        var options = new EngineKindOptions
        {
            ExecutablePath = executablePath ?? FakeEngine.DotnetHost,
            Processes = processes,
            HandshakeTimeout = TimeSpan.FromSeconds(20),
            ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(5),
            SearchTimeout = TimeSpan.FromSeconds(10),
            StopTimeout = TimeSpan.FromSeconds(2),
            ExitTimeout = exitTimeout ?? TimeSpan.FromSeconds(2),
            CheckOutTimeout = checkOutTimeout ?? TimeSpan.FromSeconds(30),
            StartAttempts = startAttempts,
            RestartDelay = restartDelay ?? TimeSpan.FromMilliseconds(100),
        };
        foreach (var argument in (string[])["exec", FakeEngine.Program, "--pid-dir", _pidDirectory, .. _behaviour])
        {
            options.Arguments.Add(argument);
        }

        return options;
    }

    /// <summary>The same options as configuration settings for a kind of the given name, as an environment or appsettings.json would give them.</summary>
    public static Dictionary<string, string?> Settings(string kind, EngineKindOptions options)
    {
        var prefix = ConfigurationPath.Combine(EnginePoolOptions.SectionName, nameof(EnginePoolOptions.Kinds), kind);
        string Key(string setting) => ConfigurationPath.Combine(prefix, setting);
        static string? Wait(TimeSpan? wait) => wait?.ToString("c", CultureInfo.InvariantCulture);

        var settings = new Dictionary<string, string?>
        {
            [Key(nameof(options.ExecutablePath))] = options.ExecutablePath,
            [Key(nameof(options.Processes))] = options.Processes.ToString(CultureInfo.InvariantCulture),
            [Key(nameof(options.HandshakeTimeout))] = Wait(options.HandshakeTimeout),
            [Key(nameof(options.ReadyTimeout))] = Wait(options.ReadyTimeout),
            [Key(nameof(options.SearchTimeout))] = Wait(options.SearchTimeout),
            [Key(nameof(options.StopTimeout))] = Wait(options.StopTimeout),
            [Key(nameof(options.ExitTimeout))] = Wait(options.ExitTimeout),
            [Key(nameof(options.CheckOutTimeout))] = Wait(options.CheckOutTimeout),
            [Key(nameof(options.StartAttempts))] = options.StartAttempts?.ToString(CultureInfo.InvariantCulture),
            [Key(nameof(options.RestartDelay))] = Wait(options.RestartDelay),
        };
        for (var i = 0; i < options.Arguments.Count; i++)
        {
            settings[ConfigurationPath.Combine(Key(nameof(options.Arguments)), i.ToString(CultureInfo.InvariantCulture))] = options.Arguments[i];
        }

        for (var i = 0; i < options.CopyEnvironment.Count; i++)
        {
            settings[ConfigurationPath.Combine(Key(nameof(options.CopyEnvironment)), i.ToString(CultureInfo.InvariantCulture))] = options.CopyEnvironment[i];
        }

        foreach (var (name, value) in options.SetEnvironment)
        {
            settings[ConfigurationPath.Combine(Key(nameof(options.SetEnvironment)), name)] = value;
        }

        return settings;
    }

    /// <summary>The id of every process of this kind that has started so far, in no order.</summary>
    public int[] StartedProcessIds() =>
        [.. Directory.GetFiles(_pidDirectory).Select(path => int.Parse(Path.GetFileName(path), CultureInfo.InvariantCulture))];

    /// <summary>Waits until <paramref name="count"/> processes have started, and returns their ids.</summary>
    public async Task<int[]> WaitForStartedAsync(int count)
    {
        await Eventually.TrueAsync(() => StartedProcessIds().Length >= count, $"{count} engine processes to start");
        return StartedProcessIds();
    }

    // A test that fails part-way may leave processes running; none outlives
    // it. A process that started after the fake wrote its file has only
    // reused the id, and is left alone.
    public void Dispose()
    {
        foreach (var path in Directory.GetFiles(_pidDirectory))
        {
            try
            {
                using var process = Process.GetProcessById(int.Parse(Path.GetFileName(path), CultureInfo.InvariantCulture));
                if (process.StartTime <= File.GetLastWriteTime(path))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // Already gone.
            }
        }

        Directory.Delete(_pidDirectory, recursive: true);
    }
}

/// <summary>Polls for a condition that becomes true in the background.</summary>
internal static class Eventually
{
    public static Task TrueAsync(Func<bool> condition, string what, int attempts = 300) =>
        TrueAsync(() => Task.FromResult(condition()), what, attempts);

    public static async Task TrueAsync(Func<Task<bool>> condition, string what, int attempts = 300)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Gave up waiting for {what}.");
    }
}
