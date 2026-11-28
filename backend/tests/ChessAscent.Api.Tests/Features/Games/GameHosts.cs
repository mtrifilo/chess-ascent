using ChessAscent.Api.Hints;
using ChessAscent.Api.Opponents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ChessAscent.Api.Tests.Features.Games;

/// <summary>Test hosts of the API with fakes in place of its engines.</summary>
internal static class GameHosts
{
    // LEARN: ConfigureTestServices runs after Program.cs has registered the
    // application's services, so what it registers replaces what the
    // application chose, for this test's host only. It is the counterpart of
    // @MockitoBean (formerly @MockBean), or of a @TestConfiguration bean
    // marked @Primary, with two differences. Nothing is a mock unless the
    // test makes it one: here it is a hand-written fake. And there is no
    // @Primary to pick between two registrations; resolving a service that
    // is registered twice gives the last registration, so RemoveAll first
    // makes the fake the only one, rather than relying on that order.
    // WithWebHostBuilder builds a separate host from the class's factory, so
    // tests that replace the engines differently can run at the same time.
    /// <summary>
    /// A host of its own, built from <paramref name="factory"/>, in which
    /// <paramref name="opponent"/> plays the computer and, when given,
    /// <paramref name="hints"/> gives the hints, with <paramref name="logs"/>
    /// capturing every log message and <paramref name="configure"/> making any
    /// other change to the services.
    /// </summary>
    public static WebApplicationFactory<Program> WithFakes(
        this ApiFactory factory,
        FakeOpponent opponent,
        FakeHintProvider? hints = null,
        CapturingLoggerProvider? logs = null,
        Action<IServiceCollection>? configure = null) =>
        factory.WithWebHostBuilder(host =>
        {
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOpponent>();
                services.AddSingleton<IOpponent>(opponent);
                if (hints is not null)
                {
                    services.RemoveAll<IHintProvider>();
                    services.AddSingleton<IHintProvider>(hints);
                }

                configure?.Invoke(services);
            });
            if (logs is not null)
            {
                host.ConfigureLogging(logging => logging
                    .AddProvider(logs)
                    .AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace));
            }
        });
}
