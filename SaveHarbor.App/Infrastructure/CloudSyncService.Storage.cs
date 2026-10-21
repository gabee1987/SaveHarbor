using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default) =>
        cloudProvider.GetConnectionStatusAsync(game, cancellationToken);

    public Task<CloudStorageUsage?> GetStorageUsageAsync(GameId game, CancellationToken cancellationToken = default) =>
        cloudProvider is ICloudStorageMaintenance maintenance
            ? maintenance.GetStorageUsageAsync(game, cancellationToken)
            : Task.FromResult<CloudStorageUsage?>(null);

    public Task<CloudUnusedFiles> FindUnusedFilesAsync(GameId game, CancellationToken cancellationToken = default) =>
        cloudProvider is ICloudStorageMaintenance maintenance
            ? maintenance.FindUnusedFilesAsync(game, cancellationToken)
            : Task.FromResult(CloudUnusedFiles.None);

    public async Task<CloudSyncResult> RemoveUnusedFilesAsync(GameId game, CloudUnusedFiles files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0 || cloudProvider is not ICloudStorageMaintenance maintenance)
        {
            return new CloudSyncResult(true, CloudSyncState.UpToDate, "There are no unused files to remove.");
        }

        await maintenance.RemoveUnusedFilesAsync(game, files, cancellationToken);
        logger.Information(AppLogKeyword.CloudSync, "Removed {Count} unused cloud file(s) for {Game}", files.Count, game);
        return new CloudSyncResult(
            true,
            CloudSyncState.UpToDate,
            $"Removed {files.Count} unused file(s) and freed {DisplayFormatter.FormatBytes(files.TotalBytes)}.");
    }
}
