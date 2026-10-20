using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// Stands in while no provider is chosen (needsSetup) or when the chosen one is not available in this build.
public sealed class NotConfiguredCloudProvider(string providerName, string message, bool needsSetup = false) : ICloudProvider
{
    public string ProviderName => providerName;

    public CloudProviderCapabilities Capabilities => CloudProviderCapabilities.None;

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateStatus());
    }

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudConnectionResult(false, CreateStatus(), message));
    }

    private CloudConnectionStatus CreateStatus() => new(false, providerName, null, message, needsSetup);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CloudWorldManifest?>(null);
    }

    public Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CloudWorldManifest>>([]);
    }

    public Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CloudSessionLock?>(null);
    }

    public Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudGroupSnapshot(null, [], new Dictionary<string, CloudSessionLock>(), []));
    }

    public Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudUploadResult(false, null, message));
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudDownloadResult(false, null, message));
    }

    public Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CloudStoredVersion>>([]);
    }

    public Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudRemovalResult(false, message));
    }

    public Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(message);
    }

    public Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CloudSessionLock?>(null);
    }

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
