using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    // Applies "Keep shared versions" after this player's successful upload. A version that cannot be removed (for
    // example one another player owns on Google Drive) is skipped and logged; it never fails the upload.
    private async Task PruneCloudVersionsAsync(GameWorld world, int latestVersionNumber, CloudSessionLock? sessionLock, CancellationToken cancellationToken)
    {
        var keep = settings.Current.CloudVersionRetentionCount;
        if (keep <= 0)
        {
            return;
        }

        try
        {
            var stored = await cloudProvider.ListStoredVersionsAsync(world.Game, world.WorldId, cancellationToken);
            var protectedVersions = new HashSet<int>();
            if (sessionLock is not null)
            {
                protectedVersions.Add(sessionLock.BasedOnVersionNumber);
            }

            foreach (var version in CloudVersionRetention.SelectForRemoval(stored, keep, latestVersionNumber, protectedVersions))
            {
                try
                {
                    await cloudProvider.RemoveVersionAsync(world.Game, world.WorldId, version.ArchiveFileName, cancellationToken);
                    logger.Information(AppLogKeyword.CloudUpload, "Removed old cloud version {VersionNumber} of world {WorldId}", version.VersionNumber, world.WorldId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.Warning(AppLogKeyword.CloudUpload, "Could not remove cloud version {VersionNumber} of world {WorldId}: {Message}", version.VersionNumber, world.WorldId, ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(AppLogKeyword.CloudUpload, "Could not list cloud versions of world {WorldId}: {Message}", world.WorldId, ex.Message);
        }
    }
}
