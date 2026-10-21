using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud;

namespace SaveHarbor.App.Infrastructure;

// Drive locks stay plain files that older SaveHarbor versions also read; locks written by them carry no DeviceId.
public sealed partial class GoogleDriveCloudProvider
{
    private const int MaxParallelLockReads = 4;

    public Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default)
    {
        return SharedFolderSessionLocks.TryAcquireAsync(
            token => GetSessionLockAsync(game, requested.WorldId, token),
            (sessionLock, token) => WriteSessionLockAsync(game, sessionLock, token),
            requested,
            verifyDelay,
            cancellationToken);
    }

    public Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return SharedFolderSessionLocks.HeartbeatAsync(
            token => GetSessionLockAsync(game, worldId, token),
            (sessionLock, token) => WriteSessionLockAsync(game, sessionLock, token),
            lockId,
            ttl,
            cancellationToken);
    }

    public async Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var manifests = await ListWorldManifestsAsync(game, cancellationToken);
        return await SharedFolderSessionLocks.ComposeSnapshotAsync(
            manifests,
            (worldId, token) => GetSessionLockAsync(game, worldId, token),
            MaxParallelLockReads,
            cancellationToken);
    }

    private async Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken)
    {
        var service = RequireConnectedService();
        await EnsureGameFolderAsync(service, game, writeMarker: true, cancellationToken);
        var locksFolderId = await FindFolderPathAsync(service, game, ["worlds", sessionLock.WorldId, "locks"], createMissing: true, cancellationToken);
        if (locksFolderId is null)
        {
            throw new InvalidOperationException("Google Drive lock folder could not be prepared.");
        }

        await UploadJsonByNameAsync(service, locksFolderId, "active-session.json", sessionLock, cancellationToken);
    }
}
