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
    string PlayHint { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
