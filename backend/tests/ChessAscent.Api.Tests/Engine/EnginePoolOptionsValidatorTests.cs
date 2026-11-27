using ChessAscent.Api.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Tests.Engine;

/// <summary>Each bad setting is reported by its configuration key.</summary>
public sealed class EnginePoolOptionsValidatorTests
{
    private const string Prefix = "EnginePool:Kinds:example:";

    private static readonly EnginePoolOptionsValidator Validator = new(Options.Create(new HostOptions()));

    [Fact]
    public void NoKindsIsValid()
    {
        Validator.Validate(null, new EnginePoolOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ACompleteKindIsValid()
    {
        Validator.Validate(null, With(Complete())).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void EverySettingWithoutADefaultIsRequired()
    {
        var result = Validator.Validate(null, With(new EngineKindOptions()));

        result.Failures.Should().BeEquivalentTo(
            Required("ExecutablePath"), Required("StartAttempts"),
            Required("HandshakeTimeout"), Required("ReadyTimeout"), Required("SearchTimeout"),
            Required("StopTimeout"), Required("ExitTimeout"), Required("CheckOutTimeout"), Required("RestartDelay"));
    }

    [Fact]
    public void CountsBelowOneAreRefused()
    {
        var result = Validator.Validate(null, With(Complete(processes: 0, startAttempts: 0)));

        result.Failures.Should().BeEquivalentTo(Prefix + "Processes must be at least 1.", Prefix + "StartAttempts must be at least 1.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WaitsMustBeGreaterThanZero(int milliseconds)
    {
        var result = Validator.Validate(null, With(Complete(searchTimeout: TimeSpan.FromMilliseconds(milliseconds))));

        result.Failures.Should().ContainSingle().Which.Should().StartWith(Prefix + "SearchTimeout is ");
    }

    [Fact]
    public void WaitsLongerThanTheClientAcceptsAreRefused()
    {
        var result = Validator.Validate(null, With(Complete(searchTimeout: TimeSpan.FromDays(25))));

        result.Failures.Should().ContainSingle().Which.Should().StartWith(Prefix + "SearchTimeout is ");
    }

    [Fact]
    public void StoppingMustFitWithinTheHostsShutdownTimeout()
    {
        // 5 s plus five times 5 s is 30 s, the host's default.
        var result = Validator.Validate(null, With(Complete(stopTimeout: TimeSpan.FromSeconds(5), exitTimeout: TimeSpan.FromSeconds(5))));

        result.Failures.Should().ContainSingle().Which.Should()
            .Contain(Prefix + "StopTimeout").And.Contain(Prefix + "ExitTimeout").And.Contain("shutdown timeout");
    }

    [Fact]
    public void MalformedEnvironmentNamesAreRefusedWithoutEchoingThem()
    {
        var kind = Complete();
        kind.CopyEnvironment.Add("");
        kind.CopyEnvironment.Add("NAME=injected");
        kind.SetEnvironment["ALSO=BAD"] = "value";

        var result = Validator.Validate(null, With(kind));

        result.Failures.Should().HaveCount(3);
        result.Failures.Should().Contain(failure => failure.StartsWith("Entry 0 (", StringComparison.Ordinal) && failure.Contains(Prefix + "CopyEnvironment is not a valid", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.StartsWith("Entry 1 (", StringComparison.Ordinal) && failure.Contains(Prefix + "CopyEnvironment is not a valid", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.StartsWith(Prefix + "SetEnvironment has a variable name", StringComparison.Ordinal));
        result.Failures.Should().NotContain(failure => failure.Contains("injected", StringComparison.Ordinal) || failure.Contains("ALSO", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEnvironmentValueWithANulCharacterIsRefusedWithoutEchoingIt()
    {
        var kind = Complete();
        kind.SetEnvironment["NAME"] = "secret\0tail";
        kind.SetEnvironment["EMPTY"] = "";

        var result = Validator.Validate(null, With(kind));

        result.Failures.Should().ContainSingle().Which.Should()
            .StartWith(Prefix + "SetEnvironment:NAME has a value that is not valid").And.NotContain("secret");
    }

    [Fact]
    public void AnInfiniteShutdownTimeoutLeavesStoppingUnchecked()
    {
        var validator = new EnginePoolOptionsValidator(Options.Create(new HostOptions { ShutdownTimeout = Timeout.InfiniteTimeSpan }));

        validator.Validate(null, With(Complete())).Succeeded.Should().BeTrue();
    }

    private static string Required(string setting) => $"{Prefix}{setting} is required.";

    private static EnginePoolOptions With(EngineKindOptions kind)
    {
        var options = new EnginePoolOptions();
        options.Kinds["example"] = kind;
        return options;
    }

    private static EngineKindOptions Complete(
        int processes = 1,
        int startAttempts = 3,
        TimeSpan? searchTimeout = null,
        TimeSpan? stopTimeout = null,
        TimeSpan? exitTimeout = null) => new()
        {
            ExecutablePath = "engine",
            Processes = processes,
            StartAttempts = startAttempts,
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            ReadyTimeout = TimeSpan.FromSeconds(5),
            SearchTimeout = searchTimeout ?? TimeSpan.FromSeconds(10),
            StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(2),
            ExitTimeout = exitTimeout ?? TimeSpan.FromSeconds(2),
            CheckOutTimeout = TimeSpan.FromSeconds(5),
            RestartDelay = TimeSpan.FromSeconds(1),
        };
}
