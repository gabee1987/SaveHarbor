using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    // One snapshot serves every world, so the world list can show each world's state, including who is playing it,
    // without a cloud round trip per world.
    public async Task<CloudWorldOverview> GetOverviewAsync(GameId game, IReadOnlyList<GameWorld> localWorlds, CancellationToken cancellationToken = default)
    {
        var connection = await cloudProvider.GetConnectionStatusAsync(game, cancellationToken);
        if (!connection.IsConnected)
        {
            return CloudWorldOverview.NotConnected;
        }

        var snapshot = await cloudProvider.GetSnapshotAsync(game, cancellationToken);
        return await BuildOverviewAsync(game, snapshot, localWorlds, cancellationToken);
    }

    // The connection, the world list and the selected world's state from one connection check and one snapshot, so a
    // refresh after a command costs one round trip instead of one per part of the window.
    public async Task<CloudRefresh> RefreshAllAsync(
        GameId game,
        IReadOnlyList<GameWorld> localWorlds,
        GameWorld? selectedWorld,
        CancellationToken cancellationToken = default)
    {
        var selected = selectedWorld?.Game == game ? selectedWorld : null;
        var connection = await cloudProvider.GetConnectionStatusAsync(game, cancellationToken);
        if (!connection.IsConnected)
        {
            var notConnected = selected is null ? null : await NotConnectedStatusAsync(selected, connection, cancellationToken);
            return new CloudRefresh(connection, CloudWorldOverview.NotConnected, notConnected);
        }

        var snapshot = await cloudProvider.GetSnapshotAsync(game, cancellationToken);
        var overview = await BuildOverviewAsync(game, snapshot, localWorlds, cancellationToken);
        if (selected is null)
        {
            return new CloudRefresh(connection, overview, null);
        }

        var manifest = snapshot.Manifests
            .Where(candidate => string.Equals(candidate.WorldId, selected.WorldId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.LatestVersion?.VersionNumber ?? -1)
            .FirstOrDefault();
        var sessionLock = snapshot.LocksByWorldId.GetValueOrDefault(selected.WorldId);
        var status = await BuildStatusAsync(selected, connection, manifest, sessionLock, cancellationToken);
        return new CloudRefresh(connection, overview, status);
    }

    private async Task<CloudWorldOverview> BuildOverviewAsync(
        GameId game,
        CloudGroupSnapshot snapshot,
        IReadOnlyList<GameWorld> localWorlds,
        CancellationToken cancellationToken)
    {
        // Manifests come from friends: ids that could not be a world are dropped, and duplicates keep the newest.
        var now = snapshot.ServerTimeUtc ?? Now;
        var manifests = snapshot.Manifests
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
            var otherPlayerPlaying = IsActiveOtherPlayerLock(snapshot.LocksByWorldId.GetValueOrDefault(world.WorldId), now);
            var hasLocalChanges = SyncStateClassifier.NeedsLocalChangeCheck(localBase, latest?.VersionNumber, otherPlayerPlaying)
                && await DetectLocalChangesAsync(localState, world, cancellationToken) == true;
            var state = SyncStateClassifier.Classify(localBase, latest?.VersionNumber, hasLocalChanges, otherPlayerPlaying);
            summaries.Add(new WorldSyncSummary(world.WorldId, state, hasLocalChanges, localBase, latest));
        }

        var localIds = localWorlds.Select(world => world.WorldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cloudOnly = manifests.Values
            .Where(manifest => !localIds.Contains(manifest.WorldId))
            .OrderByDescending(manifest => manifest.LatestVersion!.UploadedAtUtc)
            .ToArray();

        logger.Debug(AppLogKeyword.CloudSync, "Cloud overview for {Game}: {LocalCount} local world(s), {CloudOnlyCount} only in the cloud", game, summaries.Count, cloudOnly.Length);
        return new CloudWorldOverview(true, summaries, cloudOnly, snapshot.LocksByWorldId);
    }
}
