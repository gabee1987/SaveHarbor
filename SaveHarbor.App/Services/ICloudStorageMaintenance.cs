using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

// Storage use and clean-up for providers whose storage SaveHarbor manages itself (SaveHarbor Cloud).
public interface ICloudStorageMaintenance
{
    Task<CloudStorageUsage?> GetStorageUsageAsync(GameId game, CancellationToken cancellationToken = default);

    Task<CloudUnusedFiles> FindUnusedFilesAsync(GameId game, CancellationToken cancellationToken = default);

    Task RemoveUnusedFilesAsync(GameId game, CloudUnusedFiles files, CancellationToken cancellationToken = default);
}
