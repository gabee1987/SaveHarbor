using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class NotConfiguredCloudProvider : ICloudProvider
{
    public string ProviderName => "Not configured";

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CloudConnectionStatus.NotConnected(ProviderName));
    }

    public Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var status = CloudConnectionStatus.NotConnected(ProviderName);
        return Task.FromResult(new CloudConnectionResult(false, status, "Google Drive sync is not implemented yet."));
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<CloudWorldManifest?> GetWorldManifestAsync(string worldId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CloudWorldManifest?>(null);
    }

    public Task<CloudSessionLock?> GetSessionLockAsync(string worldId, CancellationToken cancellationToken = default)
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

    public Task WriteSessionLockAsync(CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearSessionLockAsync(string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
