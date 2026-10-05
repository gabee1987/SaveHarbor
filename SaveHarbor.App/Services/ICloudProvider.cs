using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudProvider
{
    string ProviderName { get; }

    Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default);

    Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default);

    Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken = default);

    Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default);
}
