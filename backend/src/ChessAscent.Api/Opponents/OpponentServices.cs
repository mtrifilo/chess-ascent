using ChessAscent.Chess;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Opponents;

public static partial class OpponentServices
{
    /// <summary>
    /// The setting that, set to <c>true</c>, makes <see cref="FirstLegalMoveOpponent"/>
    /// the computer opponent in place of Maia. As an environment variable:
    /// <c>Opponent__UseFirstLegalMove</c>. It is off unless set, and only the
    /// Development environment accepts it.
    /// </summary>
    public const string UseFirstLegalMoveSetting = "Opponent:UseFirstLegalMove";

    /// <summary>
    /// Registers the computer opponent, <see cref="IOpponent"/>, and the
    /// chess rules it checks moves with. The opponent is
    /// <see cref="MaiaOpponent"/>, played through the engine pool, whose
    /// <c>maia</c> kind is configured in <c>appsettings.json</c> and by
    /// <see cref="MaiaInstallation"/>; or, when
    /// <see cref="UseFirstLegalMoveSetting"/> is true,
    /// <see cref="FirstLegalMoveOpponent"/>, which needs no engine.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="UseFirstLegalMoveSetting"/> is true outside the Development
    /// environment, or is not <c>true</c> or <c>false</c>.
    /// </exception>
    // LEARN: The implementation behind an interface is chosen here, once,
    // while the application starts, by an ordinary `if` over a configuration
    // value read before the container is built. Spring Boot would express
    // the same choice declaratively, with @ConditionalOnProperty on two
    // @Bean methods, and evaluate it while it builds the context; .NET has no such
    // annotation, so the condition is plain code and the container only
    // ever holds the one registration made. Every class that takes an
    // IOpponent gets whichever was registered, and none of them can tell.
    //
    // LEARN: An exception thrown here, before builder.Build(), ends the
    // process before it serves anything: top-level statements have no
    // handler around them, so .NET prints the exception and exits with a
    // non-zero code, as an exception escaping a Java main method does. That
    // makes a forbidden combination of settings fail start-up rather than
    // the first request. The options validation in EngineServices fails
    // start-up too, but later, in app.RunAsync(); a check that decides which
    // services exist has to be made before they are registered.
    public static IServiceCollection AddOpponent(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Both hold no per-request state, so one instance serves every request.
        services.TryAddSingleton<IChessRules, ChessRules>();
        if (!configuration.GetValue<bool>(UseFirstLegalMoveSetting))
        {
            services.AddSingleton<IOpponent, MaiaOpponent>();
            return services;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{UseFirstLegalMoveSetting} is true, but the environment is {environment.EnvironmentName}. "
                + "The first-legal-move opponent stands in for Maia in the end-to-end test, and the API accepts it only in Development. "
                + $"Remove {UseFirstLegalMoveSetting} (the environment variable Opponent__UseFirstLegalMove), or set it to false.");
        }

        services.AddSingleton<IOpponent, FirstLegalMoveOpponent>();
        services.AddHostedService<FirstLegalMoveNotice>();
        return services;
    }

    // Says in the log, once at start-up, that the computer is not Maia, so
    // that nobody reads the games it plays as Maia's.
    private sealed partial class FirstLegalMoveNotice(ILogger<FirstLegalMoveOpponent> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LogInUse(logger, UseFirstLegalMoveSetting);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Setting} is true: the computer opponent plays the first legal move in every position, not Maia's choice")]
        private static partial void LogInUse(ILogger logger, string setting);
    }
}
