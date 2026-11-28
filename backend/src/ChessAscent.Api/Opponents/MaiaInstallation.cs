using ChessAscent.Api.Engine;

namespace ChessAscent.Api.Opponents;

/// <summary>
/// Where <c>scripts/setup-maia.sh</c> installs Maia, and the engine-pool
/// settings that point the <see cref="MaiaOpponent.EngineKind"/> kind at it.
/// </summary>
/// <remarks>
/// The script installs into <c>maia/</c> under the engines directory (see
/// <see cref="EngineInstallation"/>): the Python environment in
/// <c>venv/</c> and the downloaded weights in <c>huggingface/</c>. The
/// program, the working directory and the weights' location are computed
/// from it here; everything else about the kind is in <c>appsettings.json</c>.
/// </remarks>
public static class MaiaInstallation
{
    internal static EngineInstallation Installation { get; } = new(
        MaiaOpponent.EngineKind,
        "Maia",
        "scripts/setup-maia.sh",
        new Dictionary<string, Func<string, string>>
        {
            // The environment's own interpreter, started directly: through a
            // shell or an activation script, Python would be a grandchild of
            // the API, which the engine client cannot stop.
            [nameof(EngineKindOptions.ExecutablePath)] = maia => Path.Combine(maia, "venv", "bin", "python"),
            [nameof(EngineKindOptions.WorkingDirectory)] = maia => maia,

            // Hugging Face's cache, where the set-up script put the weights.
            // Pointing it into the installation also keeps the library away
            // from the user's own cache, token and settings.
            [ConfigurationPath.Combine(nameof(EngineKindOptions.SetEnvironment), "HF_HOME")] = maia => Path.Combine(maia, "huggingface"),
        });

    /// <summary>
    /// Adds the settings that point the Maia kind at its installation, as
    /// defaults that every other configuration source overrides.
    /// </summary>
    /// <param name="configuration">The application's configuration, which already holds the environment variables.</param>
    /// <param name="contentRootPath">The API's content root, which can give the engines directory a default.</param>
    /// <exception cref="InvalidOperationException">
    /// The engines directory is set to a relative path; or there is none,
    /// and configuration does not give the kind's program, working directory
    /// and <c>HF_HOME</c> either.
    /// </exception>
    public static void AddMaiaInstallation(this IConfigurationManager configuration, string contentRootPath) =>
        Installation.AddTo(configuration, contentRootPath);
}
