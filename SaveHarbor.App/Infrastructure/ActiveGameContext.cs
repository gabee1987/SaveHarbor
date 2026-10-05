using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Serilog;

namespace SaveHarbor.App.Infrastructure;

// Does not take IAppLogger on purpose: the logger will depend on this context (DI cycle).
public sealed class ActiveGameContext : IActiveGameContext
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IGameRegistry _registry;
    private readonly IAppDataPathProvider _pathProvider;

    public ActiveGameContext(IGameRegistry registry, IAppDataPathProvider pathProvider, GameId? startupOverride)
    {
        _registry = registry;
        _pathProvider = pathProvider;
        Current = registry.Get(startupOverride ?? ReadLastGame() ?? GameId.Windrose);
    }

    public IGameDefinition Current { get; private set; }

    public event EventHandler<IGameDefinition>? ActiveGameChanged;

    public void SetActive(GameId game)
    {
        if (Current.Id == game)
        {
            return;
        }

        Current = _registry.Get(game);
        SaveLastGame(game);
        ActiveGameChanged?.Invoke(this, Current);
    }

    private GameId? ReadLastGame()
    {
        try
        {
            if (!File.Exists(_pathProvider.AppSettingsPath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_pathProvider.AppSettingsPath));
            return GameIdExtensions.TryParseStorageKey(settings?.LastGame, out var game) ? game : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.ForContext("Keyword", "App").Warning(ex, "Could not read app settings; using the default game");
            return null;
        }
    }

    private void SaveLastGame(GameId game)
    {
        try
        {
            Directory.CreateDirectory(_pathProvider.AppDataRoot);
            var settings = new AppSettings { LastGame = game.ToStorageKey() };
            File.WriteAllText(_pathProvider.AppSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext("Keyword", "App").Warning(ex, "Could not save app settings");
        }
    }
}
