using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

public sealed class WindroseGameDefinition(GameOptionsProvider optionsProvider, WindroseSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Windrose;

    public string StorageKey => Id.ToStorageKey();

    public string DisplayName => "Windrose";

    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;

    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;

    // The launcher and the game itself. WindroseServer.exe (the dedicated server) uses its own save folder.
    public IReadOnlyList<string> ProcessNames { get; } = ["Windrose", "Windrose-Win64-Shipping"];

    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(10);

    public Uri ThemeDictionary { get; } = new("/Resources/Themes/Windrose.Colors.xaml", UriKind.Relative);

    public Uri SkinDictionary { get; } = new("/SaveHarbor.App;component/Resources/Themes/Windrose/Windrose.Theme.xaml", UriKind.Relative);

    public string? PostRestoreHint => null;

    // Steam Cloud syncs RocksDB_v2_Backups and restores files deleted while the game is closed; only a deletion the game
    // makes itself reaches Steam Cloud (docs/plans/windrose/00-overview.md finding F9).
    public string? RemoveWorldNote =>
        "Steam Cloud keeps Windrose's own copy of this world and puts it back the next time Windrose starts, so the game "
        + "will list it again. To remove it from Windrose as well, delete it in Windrose's world list afterwards.";

    public string PlayHint => "Starts a cloud session first, then launches Windrose through Steam.";

    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
