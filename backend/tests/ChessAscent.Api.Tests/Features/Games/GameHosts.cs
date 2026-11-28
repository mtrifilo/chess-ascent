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
    /// <summary>
    /// A host of its own, built from <paramref name="factory"/>, in which
    /// <paramref name="opponent"/> plays the computer and, when given,
    /// <paramref name="hints"/> gives the hints, with <paramref name="logs"/>
    /// capturing every log message and <paramref name="configure"/> making any
    /// other change to the services. See ComputerGameTests.ApiWith for how a
    /// test host replaces a service.
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
