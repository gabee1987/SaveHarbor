using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudSyncService
{
    Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudSyncStatus> RefreshStatusAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> DownloadLatestAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> DownloadLatestAvailableAsync(GameSaveRoot profile, CancellationToken cancellationToken = default);

    // Downloads a shared world that is not on this PC yet; never overwrites a local world.
    Task<CloudSyncResult> DownloadCloudWorldAsync(GameSaveRoot profile, string worldId, CancellationToken cancellationToken = default);

    // Keeps the latest shared version as a local backup, then removes the world from the shared folder for everyone.
    Task<CloudSyncResult> RemoveCloudWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default);

    Task<CloudWorldOverview> GetOverviewAsync(GameId game, IReadOnlyList<GameWorld> localWorlds, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> UploadCurrentAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> StartSessionAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> EndSessionAsync(GameWorld world, CancellationToken cancellationToken = default);
}
