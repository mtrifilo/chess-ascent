using ChessAscent.Chess;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessAscent.Api.Hints;

public static class HintServices
{
    /// <summary>
    /// Registers the source of hints, <see cref="IHintProvider"/>, played by
    /// <see cref="FairyStockfishHintProvider"/> through the engine pool, and
    /// the chess rules it checks moves with. The pool's
    /// <c>fairy_stockfish</c> kind is configured in <c>appsettings.json</c>
    /// and by <see cref="FairyStockfishInstallation"/>.
    /// </summary>
    public static IServiceCollection AddHints(this IServiceCollection services)
    {
        // Both hold no per-request state, so one instance serves every
        // request. TryAdd leaves the rules alone if the opponent has
        // registered them already.
        services.TryAddSingleton<IChessRules, ChessRules>();
        services.AddSingleton<IHintProvider, FairyStockfishHintProvider>();
        return services;
    }
}
