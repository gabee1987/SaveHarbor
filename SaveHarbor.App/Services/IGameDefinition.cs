using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public enum ProcessMatch
{
    Exact,
    Contains
}

public interface IGameDefinition
{
    GameId Id { get; }
    string StorageKey { get; }
    string DisplayName { get; }
    string LaunchUri { get; }
    string ExecutablePath { get; }
    IReadOnlyList<string> ProcessNames { get; }
    ProcessMatch ProcessMatch { get; }
    TimeSpan SaveSettleDelay { get; }
    Uri ThemeDictionary { get; }
    string? PostRestoreHint { get; }
    string PlayHint { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
