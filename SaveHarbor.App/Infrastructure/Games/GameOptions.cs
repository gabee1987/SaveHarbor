using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games;

public sealed class GameOptions
{
    public string LaunchUri { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string SaveRoot { get; init; } = string.Empty;
}

// appsettings.json values per game; a save folder chosen in Settings (IAppSettingsStore) overrides SaveRoot.
public sealed class GameOptionsProvider(IReadOnlyDictionary<GameId, GameOptions> optionsByGame, IAppSettingsStore? settings = null)
{
    public GameOptions Get(GameId game)
    {
        var options = optionsByGame.TryGetValue(game, out var configured) ? configured : new GameOptions();
        return settings is not null &&
               settings.Current.SaveRootOverrides.TryGetValue(game.ToStorageKey(), out var overrideRoot) &&
               !string.IsNullOrWhiteSpace(overrideRoot)
            ? new GameOptions { LaunchUri = options.LaunchUri, ExecutablePath = options.ExecutablePath, SaveRoot = overrideRoot }
            : options;
    }
}
