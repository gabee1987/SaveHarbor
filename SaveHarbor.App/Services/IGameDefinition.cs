using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IGameDefinition
{
    GameId Id { get; }
    string StorageKey { get; }
    string DisplayName { get; }
    string LaunchUri { get; }
    string ExecutablePath { get; }
    // Exact process names (without ".exe") that mean the game is running.
    IReadOnlyList<string> ProcessNames { get; }
    TimeSpan SaveSettleDelay { get; }
    Uri ThemeDictionary { get; }

    // The game's skin for the shared main screen and its windows (styles, ornaments, effects).
    Uri SkinDictionary { get; }
    string? PostRestoreHint { get; }

    // What removing a world from this PC cannot do for this game (for example Steam Cloud bringing it back), shown in
    // the removal confirmation and the activity log. Null when there is nothing to add.
    string? RemoveWorldNote { get; }

    // When set for a world, SaveHarbor does not delete its files itself, because the game's own sync would bring them
    // back (Enshrouded and Steam Cloud). Removing it from this PC then shows this text, which says where to delete it.
    string? GetRemoveWorldInGameHint(GameWorld world);

    // Which of the game's save folders SaveHarbor manages, shown under the save folder in Settings. Null when the
    // folder needs no explanation.
    string? SaveFolderHint { get; }
    string PlayHint { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
