using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games;

public sealed class GameOptions
{
    public string LaunchUri { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string SaveRoot { get; init; } = string.Empty;
}

public sealed class GameOptionsProvider(IReadOnlyDictionary<GameId, GameOptions> optionsByGame)
{
    public GameOptions Get(GameId game) =>
        optionsByGame.TryGetValue(game, out var options) ? options : new GameOptions();
}
