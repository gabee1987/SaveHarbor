using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    public async Task<DateTimeOffset?> GetPendingUploadAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        return localState.PendingUploadSinceUtc;
    }

    public async Task ClearPendingUploadAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        if (localState.PendingUploadSinceUtc is null)
        {
            return;
        }

        localState.PendingUploadSinceUtc = null;
        await localSyncStateService.SaveAsync(localState, cancellationToken);
        logger.Information(AppLogKeyword.CloudUpload, "Pending upload cleared for world {WorldId}", world.WorldId);
    }
}
