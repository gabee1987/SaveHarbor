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

    public Uri ThemeDictionary { get; } = new("/Resources/Themes/Enshrouded.Colors.xaml", UriKind.Relative);

    public Uri SkinDictionary { get; } = new("/SaveHarbor.App;component/Resources/Themes/Enshrouded/Enshrouded.Theme.xaml", UriKind.Relative);

    // SaveHarbor manages the game's Steam Cloud folder (EnshroudedSaveRootLocator), shown as the "SAVEGAMES:STEAM" tab.
    public string? PostRestoreHint => "Open this world from the SAVEGAMES:STEAM tab in Enshrouded.";

    // Only worlds Steam does not track are removed by SaveHarbor (see GetRemoveWorldInGameHint).
    public string? RemoveWorldNote =>
        "Steam Cloud does not know this world file, so Enshrouded does not list it and Steam will not bring it back. "
        + "SaveHarbor keeps new worlds out of this slot, because Steam would hide them from the game as well.";

    // Steam Cloud restores a world file deleted from outside the game the next time the game starts (seen on the
    // owner's PC, 2026-10-28), so a world Steam tracks is only removed for good inside the game. A file Steam does not
    // track, such as a download into the slot of a world deleted in the game, is not listed by the game and not
    // restored by Steam, so SaveHarbor removes that one itself. When Steam's list cannot be read, the game decides.
    public string? GetRemoveWorldInGameHint(GameWorld world) =>
        EnshroudedSteamCloudCache.IsTracked(world.SavePath) == false
            ? null
            : "Enshrouded keeps this world in Steam Cloud, so if SaveHarbor deleted the file, Steam would bring it back the next time the game starts.\n\n"
              + "Delete the world inside Enshrouded instead, on the SAVEGAMES:STEAM tab. Steam then removes its cloud copy too, and SaveHarbor notices that the world is gone.\n\n"
              + "To keep a copy, make a backup first: Backups tab → Back up now. The shared cloud copy is not touched either way.";

    public string? SaveFolderHint =>
        "The Steam Cloud folder of the Steam account signed in on this PC (the SAVEGAMES:STEAM tab in the game), so Steam backs up "
        + "every world SaveHarbor places here. Worlds on the game's Local tab are not shown; to share one, use Import… on the Backups tab.";

    public string PlayHint => "Play hosts this world for your group. Join only starts the game, so you can join a friend's world.";

    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
