using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Features.Games;

public static class GameServices
{
    /// <summary>Registers <see cref="GameStore"/>, which saves and loads games.</summary>
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        // LEARN: TryAddSingleton registers the service only if nothing is
        // registered for its type yet, where AddSingleton would add a second
        // registration; when a type has several, resolving it returns the
        // last. Spring's nearest equivalent is @ConditionalOnMissingBean.
        // So a clock registered earlier in Program.cs is kept, and a test's,
        // which WebApplicationFactory adds after Program.cs has run, replaces
        // this one by coming last.
        services.TryAddSingleton(TimeProvider.System);

        // Scoped, because it uses the request's AppDbContext.
        services.AddScoped<GameStore>();

        return services;
    }
}
