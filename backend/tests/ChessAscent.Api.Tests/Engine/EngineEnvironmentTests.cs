using ChessAscent.Api.Engine;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>
/// An engine starts with exactly the environment its kind configures: the
/// variables named for copying from the application's own environment, and
/// those set to given values, and nothing else.
/// </summary>
/// <remarks>
/// Each test sets variables of its own, under names no other test uses, in
/// this process's environment, which every test shares, and removes them
/// afterwards.
/// </remarks>
public sealed class EngineEnvironmentTests : IDisposable
{
    private const string Kind = "fake";

    private readonly string _prefix = "CHA17_TEST_" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly List<string> _set = [];
    private readonly EnvironmentDump _dump = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VariableTheApplicationHasButTheKindDoesNotListDoesNotReachTheEngine()
    {
        var secret = SetInThisProcess("SECRET", "connection-string-stand-in");

        var environment = await EngineEnvironmentAsync(copy: [], set: []);

        environment.Should().NotContainKey(secret);
        environment.Should().BeEmpty("an engine with nothing configured gets an empty environment");
    }

    [Fact]
    public async Task ListedVariableIsCopiedWithTheApplicationsValue()
    {
        var copied = SetInThisProcess("COPIED", "from the application");

        var environment = await EngineEnvironmentAsync(copy: [copied], set: []);

        environment.Should().Equal(new Dictionary<string, string> { [copied] = "from the application" });
    }

    [Fact]
    public async Task SetVariableReachesTheEngineWithTheGivenValue()
    {
        var given = _prefix + "GIVEN";

        var environment = await EngineEnvironmentAsync(copy: [], set: new() { [given] = "given value" });

        environment.Should().Equal(new Dictionary<string, string> { [given] = "given value" });
    }

    [Fact]
    public async Task SetValueWinsOverACopiedOne()
    {
        var both = SetInThisProcess("BOTH", "from the application");

        var environment = await EngineEnvironmentAsync(copy: [both], set: new() { [both] = "from the kind" });

        environment.Should().Equal(new Dictionary<string, string> { [both] = "from the kind" });
    }

    [Fact]
    public async Task ListedVariableTheApplicationDoesNotHaveIsLeftOut()
    {
        var missing = _prefix + "MISSING";
        var present = SetInThisProcess("PRESENT", "here");

        var environment = await EngineEnvironmentAsync(copy: [missing, present], set: []);

        environment.Should().Equal(new Dictionary<string, string> { [present] = "here" });
    }

    [Fact]
    public async Task NoVariableValueIsLogged()
    {
        var copied = SetInThisProcess("COPIED", "copied-value-never-logged");
        var given = _prefix + "GIVEN";
        var poolLogger = new ListLogger<EnginePool>();
        var engineLogger = new ListLogger<UciEngine>();

        await EngineEnvironmentAsync(copy: [copied], set: new() { [given] = "set-value-never-logged" }, poolLogger, engineLogger);

        poolLogger.Entries.Concat(engineLogger.Entries).Should().NotContain(entry =>
            entry.Message.Contains("never-logged", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var name in _set)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        _dump.Dispose();
    }

    // Sets a variable of this test's own in this process, and returns its name.
    private string SetInThisProcess(string suffix, string value)
    {
        var name = _prefix + suffix;
        Environment.SetEnvironmentVariable(name, value);
        _set.Add(name);
        return name;
    }

    // Starts a pool of one fake engine with the given environment settings,
    // waits until a caller has had the engine, and returns the environment
    // the engine reported.
    private async Task<Dictionary<string, string>> EngineEnvironmentAsync(
        string[] copy,
        Dictionary<string, string> set,
        ListLogger<EnginePool>? poolLogger = null,
        ListLogger<UciEngine>? engineLogger = null)
    {
        using var fake = new FakeEngineKind(_dump.Arguments);
        var kind = fake.Options();
        foreach (var name in copy)
        {
            kind.CopyEnvironment.Add(name);
        }

        foreach (var (name, value) in set)
        {
            kind.SetEnvironment[name] = value;
        }

        var options = new EnginePoolOptions();
        options.Kinds[Kind] = kind;
        await using (var pool = new EnginePool(Options.Create(options), poolLogger ?? new(), engineLogger ?? new()))
        {
            pool.Start();
            await pool.RunAsync(Kind, lease => lease.Engine.SearchAsync(UciPosition.StartingPosition([]), SearchLimit.Depth(1), Token), Token);
        }

        return await _dump.ReadAsync([.. copy, .. set.Keys]);
    }
}
