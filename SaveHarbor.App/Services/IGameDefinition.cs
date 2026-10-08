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
    string PlayHint { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
