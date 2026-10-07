using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    // One listing of the shared folder serves every world, so the world list can show each world's state without a
    // cloud round trip per world.
    public async Task<CloudWorldOverview> GetOverviewAsync(GameId game, IReadOnlyList<GameWorld> localWorlds, CancellationToken cancellationToken = default)
    {
        var connection = await cloudProvider.GetConnectionStatusAsync(game, cancellationToken);
        if (!connection.IsConnected)
        {
            return CloudWorldOverview.NotConnected;
        }

        // Manifests come from friends: ids that could not be a world are dropped, and duplicates keep the newest.
        var manifests = (await cloudProvider.ListWorldManifestsAsync(game, cancellationToken))
            .Where(manifest => manifest.LatestVersion is not null && SafePath.IsSafeSegment(manifest.WorldId))
            .GroupBy(manifest => manifest.WorldId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(manifest => manifest.LatestVersion!.VersionNumber).First())
            .ToDictionary(manifest => manifest.WorldId, StringComparer.OrdinalIgnoreCase);

        var summaries = new List<WorldSyncSummary>(localWorlds.Count);
        foreach (var world in localWorlds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = manifests.GetValueOrDefault(world.WorldId)?.LatestVersion;
            var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
            var localBase = localState.LocalBaseVersionNumber;
            var hasLocalChanges = SyncStateClassifier.NeedsLocalChangeCheck(localBase, latest?.VersionNumber, otherPlayerPlaying: false)
                && await DetectLocalChangesAsync(localState, world, cancellationToken) == true;
            var state = SyncStateClassifier.Classify(localBase, latest?.VersionNumber, hasLocalChanges, otherPlayerPlaying: false);
            summaries.Add(new WorldSyncSummary(world.WorldId, state, hasLocalChanges, localBase, latest));
        }

        var localIds = localWorlds.Select(world => world.WorldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cloudOnly = manifests.Values
            .Where(manifest => !localIds.Contains(manifest.WorldId))
            .OrderByDescending(manifest => manifest.LatestVersion!.UploadedAtUtc)
            .ToArray();

        logger.Debug(AppLogKeyword.CloudSync, "Cloud overview for {Game}: {LocalCount} local world(s), {CloudOnlyCount} only in the cloud", game, summaries.Count, cloudOnly.Length);
        return new CloudWorldOverview(true, summaries, cloudOnly);
    }
}
