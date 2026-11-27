using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Features.Games;

public static class GameServices
{
    /// <summary>Registers <see cref="GameStore"/>, which saves and loads games.</summary>
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        // TryAdd leaves alone a clock that is already registered, such as a
        // test's.
        services.TryAddSingleton(TimeProvider.System);

        // Scoped, because it uses the request's AppDbContext.
        services.AddScoped<GameStore>();

        return services;
    }
}
