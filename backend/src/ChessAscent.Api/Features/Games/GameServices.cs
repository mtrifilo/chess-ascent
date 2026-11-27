using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Features.Games;

public static class GameServices
{
    /// <summary>Registers <see cref="GameStore"/>, which saves and loads games.</summary>
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        // LEARN: TryAddSingleton registers the service only if nothing is
        // registered for its type yet. AddSingleton would add a second
        // registration, and resolving a type that has several returns the
        // last one added. Spring's nearest equivalent is
        // @ConditionalOnMissingBean.
        services.TryAddSingleton(TimeProvider.System);

        // Scoped, because it uses the request's AppDbContext.
        services.AddScoped<GameStore>();

        return services;
    }
}
