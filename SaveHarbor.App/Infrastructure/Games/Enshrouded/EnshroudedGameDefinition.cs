using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

public sealed class EnshroudedGameDefinition(GameOptionsProvider optionsProvider, EnshroudedSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Enshrouded;

    public string StorageKey => Id.ToStorageKey();

    public string DisplayName => "Enshrouded";

    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;

    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;

    public IReadOnlyList<string> ProcessNames { get; } = ["enshrouded"];

    // The game writes the world and a rolling copy of it when it closes.
    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(15);

    // The Enshrouded colours and skin follow with the theme work; until then the game wears the Windrose look.
    public Uri ThemeDictionary { get; } = new("/Resources/Themes/Windrose.Colors.xaml", UriKind.Relative);

    public Uri SkinDictionary { get; } = new("/SaveHarbor.App;component/Resources/Themes/Windrose/Windrose.Theme.xaml", UriKind.Relative);

    // SaveHarbor writes shared worlds into the game's Local save folder, never into the Steam Cloud one.
    public string? PostRestoreHint => "Open this world from the Local tab in Enshrouded.";

    public string? RemoveWorldNote => "If the world also exists in your Steam Cloud tab, Enshrouded keeps that copy.";

    public string PlayHint => "Play hosts this world for your group. Join only starts the game, so you can join a friend's world.";

    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
