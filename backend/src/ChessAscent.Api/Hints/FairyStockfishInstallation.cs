using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Hints;

/// <summary>
/// Where <c>scripts/setup-fairy-stockfish.sh</c> installs Fairy-Stockfish,
/// and the engine-pool settings that point the
/// <see cref="FairyStockfishHintProvider.EngineKind"/> kind at it.
/// </summary>
/// <remarks>
/// The script installs into <c>fairy_stockfish/</c> under the engines
/// directory (see <see cref="EngineInstallation"/>): the program itself, and
/// the source it was built from in <c>source/</c>. The program and the
/// working directory are computed from it here; everything else about the
/// kind is in <c>appsettings.json</c>.
/// </remarks>
public static class FairyStockfishInstallation
{
    private static readonly EngineInstallation Installation = new(
        FairyStockfishHintProvider.EngineKind,
        FairyStockfishHintProvider.EngineName,
        "scripts/setup-fairy-stockfish.sh",
        new Dictionary<string, Func<string, string>>
        {
            // The program the script built, started directly.
            [nameof(EngineKindOptions.ExecutablePath)] = fairyStockfish => Path.Combine(fairyStockfish, "fairy-stockfish"),

            // The engine reads and writes no file unless an option names one
            // (a debug log, an evaluation file); a relative name is then
            // inside its own directory, not the API's.
            [nameof(EngineKindOptions.WorkingDirectory)] = fairyStockfish => fairyStockfish,
        });

    /// <summary>
    /// Adds the settings that point the Fairy-Stockfish kind at its
    /// installation, as defaults that every other configuration source
    /// overrides.
    /// </summary>
    /// <param name="configuration">The application's configuration, which already holds the environment variables.</param>
    /// <param name="contentRootPath">The API's content root, which can give the engines directory a default.</param>
    /// <exception cref="InvalidOperationException">
    /// The engines directory is set to a relative path; or there is none,
    /// and configuration does not give the kind's program and working
    /// directory either.
    /// </exception>
    public static void AddFairyStockfishInstallation(this IConfigurationManager configuration, string contentRootPath) =>
        Installation.AddTo(configuration, contentRootPath);
}
