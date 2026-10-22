using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ILocalSyncStateService
{
    Task<LocalSyncState> LoadAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken = default);
}
