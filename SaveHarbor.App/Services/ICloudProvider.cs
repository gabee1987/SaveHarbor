using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudProvider
{
    string ProviderName { get; }

    CloudProviderCapabilities Capabilities { get; }

    Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    // Manifests and locks of every world of a game in one call.
    Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default);

    Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default);

    // Version archives currently stored for a world (names only; nothing is downloaded).
    Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    // Removes one version archive and its metadata. Providers with a recycle bin move it there instead of deleting.
    Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default);

    // Removes a world with all its versions and locks. Providers with a recycle bin move it there instead of deleting.
    Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    // Stores the requested lock unless another device holds a live one. Current is the winner either way.
    Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default);

    // Extends this device's lock by ttl from now. Null when the lock is gone or someone else holds it now.
    Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default);
}
