using System.Globalization;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Engine;

// LEARN: IValidateOptions<T> is the options pattern's validation hook: the
// counterpart of putting @Validated on a @ConfigurationProperties class,
// except that the rules are code rather than annotations. Validate returns
// every failure at once instead of throwing at the first. ValidateOnStart()
// in EngineServices makes the host run it while it starts, before any hosted
// service, so a bad value stops the application at start-up with an
// OptionsValidationException that lists the failures; without it, validation
// would wait until something first read IOptions<T>.Value.
/// <summary>
/// Checks the engine pool's options, naming each failing setting by its
/// configuration key, such as <c>EnginePool:Kinds:example:Processes</c>.
/// </summary>
internal sealed class EnginePoolOptionsValidator(IOptions<HostOptions> host) : IValidateOptions<EnginePoolOptions>
{
    public ValidateOptionsResult Validate(string? name, EnginePoolOptions options)
    {
        var failures = new List<string>();
        foreach (var (kind, engine) in options.Kinds)
        {
            ValidateKind(ConfigurationPath.Combine(EnginePoolOptions.SectionName, nameof(options.Kinds), kind), engine, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateKind(string path, EngineKindOptions? engine, List<string> failures)
    {
        // Configuration always binds a kind to an instance; a null one can
        // only come from code that builds the options itself.
        if (engine is null)
        {
            failures.Add($"{path} has no settings.");
            return;
        }

        string Key(string setting) => ConfigurationPath.Combine(path, setting);

        if (string.IsNullOrWhiteSpace(engine.ExecutablePath))
        {
            failures.Add($"{Key(nameof(engine.ExecutablePath))} is required.");
        }

        // A failure names the setting, never a value, and not a name in the
        // copy list either, since a malformed one could be anything.
        for (var i = 0; i < engine.CopyEnvironment.Count; i++)
        {
            if (!UciEngineOptions.IsValidEnvironmentName(engine.CopyEnvironment[i]))
            {
                failures.Add($"{Key(ConfigurationPath.Combine(nameof(engine.CopyEnvironment), i.ToString(CultureInfo.InvariantCulture)))} "
                    + "is not a valid environment variable name: it must be non-empty, with no '=' or NUL character.");
            }
        }

        foreach (var (name, value) in engine.SetEnvironment)
        {
            if (!UciEngineOptions.IsValidEnvironmentName(name))
            {
                failures.Add($"{Key(nameof(engine.SetEnvironment))} has a variable name that is not valid: it must be non-empty, with no '=' or NUL character.");
            }
            else if (!UciEngineOptions.IsValidEnvironmentValue(value))
            {
                failures.Add($"{Key(ConfigurationPath.Combine(nameof(engine.SetEnvironment), name))} has a value that is not valid: it must be set, with no NUL character.");
            }
        }

        if (engine.Processes < 1)
        {
            failures.Add($"{Key(nameof(engine.Processes))} must be at least 1.");
        }

        if (engine.StartAttempts is null)
        {
            failures.Add($"{Key(nameof(engine.StartAttempts))} is required.");
        }
        else if (engine.StartAttempts < 1)
        {
            failures.Add($"{Key(nameof(engine.StartAttempts))} must be at least 1.");
        }

        ValidateWait(engine.HandshakeTimeout, Key(nameof(engine.HandshakeTimeout)), failures);
        ValidateWait(engine.ReadyTimeout, Key(nameof(engine.ReadyTimeout)), failures);
        ValidateWait(engine.SearchTimeout, Key(nameof(engine.SearchTimeout)), failures);
        ValidateWait(engine.StopTimeout, Key(nameof(engine.StopTimeout)), failures);
        ValidateWait(engine.ExitTimeout, Key(nameof(engine.ExitTimeout)), failures);
        ValidateWait(engine.CheckOutTimeout, Key(nameof(engine.CheckOutTimeout)), failures);
        ValidateWait(engine.RestartDelay, Key(nameof(engine.RestartDelay)), failures);

        // The pool stops all its engines at once. For every process to be
        // gone before the host gives up on shutting down, the longest an
        // engine can take to stop has to fit in the host's time.
        if (engine.StopTimeout > TimeSpan.Zero && engine.ExitTimeout > TimeSpan.Zero
            && host.Value.ShutdownTimeout != Timeout.InfiniteTimeSpan)
        {
            var longestStop = UciEngineOptions.LongestDisposal(engine.StopTimeout.Value, engine.ExitTimeout.Value);
            if (longestStop >= host.Value.ShutdownTimeout)
            {
                failures.Add(
                    $"With {Key(nameof(engine.StopTimeout))} and {Key(nameof(engine.ExitTimeout))} as set, stopping an engine can take {longestStop}, "
                    + $"which must be less than the host's shutdown timeout of {host.Value.ShutdownTimeout}.");
            }
        }
    }

    private static void ValidateWait(TimeSpan? wait, string key, List<string> failures)
    {
        if (wait is null)
        {
            failures.Add($"{key} is required.");
        }
        else if (!UciEngineOptions.IsValidWait(wait.Value))
        {
            failures.Add($"{key} is {wait}; it must be greater than zero and at most {UciEngineOptions.LongestWait}.");
        }
    }
}
