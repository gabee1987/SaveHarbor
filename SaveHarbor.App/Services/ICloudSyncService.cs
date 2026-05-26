using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudSyncService
{
    Task<CloudSyncStatus> RefreshStatusAsync(WindroseWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> DownloadLatestAsync(WindroseWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> UploadCurrentAsync(WindroseWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> StartSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default);

    Task<CloudSyncResult> EndSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default);
}
