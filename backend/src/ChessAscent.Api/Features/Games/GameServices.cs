using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Features.Games;

public static class GameServices
{
    /// <summary>Registers <see cref="GameStore"/>, which saves and loads games.</summary>
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        // LEARN: TryAddSingleton registers the service only if nothing is
        // registered for its type yet, where AddSingleton would add a second
        // registration that wins on resolve. Spring's nearest equivalent is
        // @ConditionalOnMissingBean. It leaves alone a clock registered
        // earlier, such as a test's.
        services.TryAddSingleton(TimeProvider.System);

        // Scoped, because it uses the request's AppDbContext.
        services.AddScoped<GameStore>();

        return services;
    }
}
