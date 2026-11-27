using ChessAscent.Chess;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Opponents;

public static class OpponentServices
{
    /// <summary>
    /// Registers the computer opponent, <see cref="IOpponent"/>, played by
    /// <see cref="MaiaOpponent"/> through the engine pool, and the chess
    /// rules it checks moves with. The pool's <c>maia</c> kind is configured
    /// in <c>appsettings.json</c> and by <see cref="MaiaInstallation"/>.
    /// </summary>
    public static IServiceCollection AddOpponent(this IServiceCollection services)
    {
        // Both hold no per-request state, so one instance serves every request.
        services.TryAddSingleton<IChessRules, ChessRules>();
        services.AddSingleton<IOpponent, MaiaOpponent>();
        return services;
    }
}
