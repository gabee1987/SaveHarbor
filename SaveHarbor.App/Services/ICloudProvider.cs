using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudProvider
{
    string ProviderName { get; }

    Task<CloudConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<CloudWorldManifest?> GetWorldManifestAsync(string worldId, CancellationToken cancellationToken = default);

    Task<CloudSessionLock?> GetSessionLockAsync(string worldId, CancellationToken cancellationToken = default);

    Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default);

    Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default);

    Task WriteSessionLockAsync(CloudSessionLock sessionLock, CancellationToken cancellationToken = default);

    Task ClearSessionLockAsync(string worldId, string lockId, CancellationToken cancellationToken = default);
}
