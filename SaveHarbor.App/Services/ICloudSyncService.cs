using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudSyncService
{
    Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);

    Task<CloudSyncStatus> RefreshStatusAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> DownloadLatestAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> DownloadLatestAvailableAsync(GameSaveRoot profile, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> UploadCurrentAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> StartSessionAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> EndSessionAsync(GameWorld world, CancellationToken cancellationToken = default);
}
