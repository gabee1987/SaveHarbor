using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// Does not take IAppLogger on purpose: the logger will depend on this context (DI cycle).
public sealed class ActiveGameContext : IActiveGameContext
{
    private readonly IGameRegistry _registry;
    private readonly IAppSettingsStore _settings;

    public ActiveGameContext(IGameRegistry registry, IAppSettingsStore settings, GameId? startupOverride)
    {
        _registry = registry;
        _settings = settings;
        var lastGame = GameIdExtensions.TryParseStorageKey(settings.Current.LastGame, out var saved) ? saved : (GameId?)null;
        Current = registry.Get(startupOverride ?? lastGame ?? GameId.Windrose);
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
        _settings.Update(settings => settings.LastGame = game.ToStorageKey());
        ActiveGameChanged?.Invoke(this, Current);
    }
}
