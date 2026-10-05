using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public sealed class DragonwildsGameDefinition(GameOptionsProvider optionsProvider, DragonwildsSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Dragonwilds;

    public string StorageKey => Id.ToStorageKey();

    public string DisplayName => "RuneScape: Dragonwilds";

    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;

    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;

    public IReadOnlyList<string> ProcessNames { get; } = ["RSDragonwilds-Win64-Shipping", "RSDragonwilds", "RSDragonwildsServer"];

    public ProcessMatch ProcessMatch => ProcessMatch.Exact;

    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(15);

    public Uri ThemeDictionary { get; } = new("/Resources/Themes/Dragonwilds.Colors.xaml", UriKind.Relative);

    public string? PostRestoreHint => "If Steam asks about a cloud conflict, choose Local files.";

    public string PlayHint => "Play hosts this world for your group. Join only starts the game, so you can join a friend's world.";

    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
