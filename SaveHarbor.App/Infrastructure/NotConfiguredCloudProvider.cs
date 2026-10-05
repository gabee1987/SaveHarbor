using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class NotConfiguredCloudProvider : ICloudProvider
{
    public string ProviderName => "Not configured";

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CloudConnectionStatus.NotConnected(ProviderName));
    }

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var status = CloudConnectionStatus.NotConnected(ProviderName);
        return Task.FromResult(new CloudConnectionResult(false, status, "Google Drive sync is not implemented yet."));
    }

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

    public Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudUploadResult(false, null, "Cloud provider is not configured."));
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudDownloadResult(false, null, "Cloud provider is not configured."));
    }

    public Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CloudStoredVersion>>([]);
    }

    public Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
