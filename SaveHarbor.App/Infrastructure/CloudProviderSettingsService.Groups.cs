using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

// SaveHarbor Cloud groups in the same settings file as the Google Drive folders, so one class owns that file.
public sealed partial class CloudProviderSettingsService
{
    public string? GetActiveGroupId(GameId game) =>
        ReadExistingSettings().ActiveGroups.GetValueOrDefault(game.ToStorageKey()) is { Length: > 0 } groupId ? groupId : null;

    public IReadOnlyList<SavedCloudGroup> GetKnownGroups() => ReadExistingSettings().KnownGroups;

    public async Task SetActiveGroupsAsync(IReadOnlyCollection<GameId> games, string groupId, CancellationToken cancellationToken = default)
    {
        var settings = ReadExistingSettings();
        settings.SchemaVersion = 3;
        foreach (var game in games)
        {
            settings.ActiveGroups[game.ToStorageKey()] = groupId;
        }

        await WriteSettingsAsync(settings, cancellationToken);
        logger.Information(AppLogKeyword.CloudProvider, "SaveHarbor Cloud group chosen for {Games}", string.Join(", ", games));
    }

    // Known groups replace the stored list; active groups the account no longer belongs to are dropped.
    public async Task SaveKnownGroupsAsync(IReadOnlyList<SavedCloudGroup> groups, CancellationToken cancellationToken = default)
    {
        var settings = ReadExistingSettings();
        settings.SchemaVersion = 3;
        settings.KnownGroups = groups.ToList();
        var ids = groups.Select(group => group.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var game in settings.ActiveGroups.Where(entry => !ids.Contains(entry.Value)).Select(entry => entry.Key).ToArray())
        {
            settings.ActiveGroups.Remove(game);
        }

        await WriteSettingsAsync(settings, cancellationToken);
    }

    // After sign-out the groups belong to nobody on this PC.
    public async Task ClearGroupsAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadExistingSettings();
        if (settings.ActiveGroups.Count == 0 && settings.KnownGroups.Count == 0)
        {
            return;
        }

        settings.ActiveGroups.Clear();
        settings.KnownGroups.Clear();
        await WriteSettingsAsync(settings, cancellationToken);
    }
}
