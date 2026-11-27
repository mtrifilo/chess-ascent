using ChessAscent.Api.ApiDocument;
using ChessAscent.Api.Features.Health;
using Microsoft.Extensions.Options;

namespace ChessAscent.Api.Engine;

public static class EngineServices
{
    // The name the engine check reports under in logs.
    private const string HealthCheckName = "engines";

    /// <summary>
    /// Registers the engine pool, its options from the <c>EnginePool</c>
    /// configuration section, the hosted service that starts and stops it,
    /// and a readiness check that every configured kind has a working process.
    /// </summary>
    public static IServiceCollection AddEnginePool(this IServiceCollection services)
    {
        // LEARN: AddOptions<T>().BindConfiguration(section) is the counterpart
        // of @ConfigurationProperties(prefix = "EnginePool") with
        // @EnableConfigurationProperties: it binds the section to the class,
        // and code takes IOptions<EnginePoolOptions> to read the result.
        // ErrorOnUnknownConfiguration makes a key that matches no property,
        // such as a misspelt "Proceses", fail the binding instead of being
        // ignored, which Spring's ignoreUnknownFields = false also does. A
        // value that cannot be converted ("soon" for a TimeSpan) fails too.
        // Either way the start-up error is an InvalidOperationException whose
        // inner exception names the key. ValidateOnStart runs the
        // registered IValidateOptions<T> while the host starts, so a bad
        // configuration stops start-up rather than the first request that
        // needs an engine.
        services.AddOptions<EnginePoolOptions>()
            .BindConfiguration(EnginePoolOptions.SectionName, binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EnginePoolOptions>, EnginePoolOptionsValidator>();

        // LEARN: Singleton, Scoped and Transient correspond to Spring's
        // singleton scope, request scope and prototype scope. The pool is a
        // Singleton: one instance for the life of the application, which owns
        // processes that outlive every request. That means it must not depend
        // on anything Scoped. A Singleton resolves its constructor's
        // dependencies once, so a Scoped one (an AppDbContext, or
        // IOptionsSnapshot<T>) would be the instance of whichever scope
        // happened to create the pool, kept long after that scope was
        // disposed and then shared by every request at once: the "captured
        // dependency" bug. Spring avoids it by injecting a scoped proxy that
        // looks up the current request's instance on each call; .NET injects
        // no proxy, so the object you get is the object you keep. In the
        // Development environment (and so in the tests) the container
        // checks this when it is built and refuses a Singleton that depends
        // on a Scoped service; elsewhere the check is off, for speed. The
        // pool takes only Singletons: IOptions<T> (read once, not reloaded)
        // and loggers. Something that needs a Scoped service from a
        // Singleton creates a scope of its own for each use, as
        // StartupMigrations does.
        services.AddSingleton<EnginePool>();

        // The build-time OpenAPI generator runs Program.cs without a real
        // server (see ApiDocumentServices), and must start no engines.
        if (!ApiDocumentServices.IsBuildTimeGeneration)
        {
            services.AddHostedService<EnginePoolService>();
        }

        services.AddSingleton<EnginePoolReadinessCheck>();
        services.AddHealthChecks()
            .AddCheck<EnginePoolReadinessCheck>(HealthCheckName, tags: [HealthEndpoints.ReadinessTag]);

        return services;
    }
}
