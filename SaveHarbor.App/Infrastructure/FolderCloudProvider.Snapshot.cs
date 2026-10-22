using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class FolderCloudProvider
{
    public async Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var manifests = await ListWorldManifestsAsync(game, cancellationToken);
        return await SharedFolderSessionLocks.ComposeSnapshotAsync(
            manifests,
            (worldId, token) => GetSessionLockAsync(game, worldId, token),
            maxParallelReads: 1,
            cancellationToken);
    }
}
