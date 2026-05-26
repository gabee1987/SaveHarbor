using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ILocalSyncStateService
{
    Task<LocalSyncState> LoadAsync(WindroseWorld world, CancellationToken cancellationToken = default);

    Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken = default);
}
