using System.IO;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

// A world in a save folder SaveHarbor only reads (Enshrouded's Steam Cloud folder) is never written to. A download of it
// goes to the folder SaveHarbor may write to: over the copy there when it has one, otherwise as a new world. Once that
// copy exists it is the one that is played and shared; both copies share one sync state (keyed by world id), so a
// session or upload from the older read-only copy could replace newer progress in the cloud.
public sealed partial class CloudSyncService
{
    private const string UseWritableCopyMessage =
        "This world also has a copy in the folder SaveHarbor manages. Select that copy to play or share it. Nothing was changed.";

    private async Task<CloudSyncResult> DownloadIntoWritableRootAsync(GameWorld world, CancellationToken cancellationToken)
    {
        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        var profile = (await adapter.DiscoverSaveRootsAsync(cancellationToken)).FirstOrDefault(root => root.CanReceiveWorlds);
        if (profile is null)
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "No save folder was found that SaveHarbor may place this world in. Nothing was changed.");
        }

        if (await FindWritableCopyAsync(world, cancellationToken) is { } copy)
        {
            logger.Information(AppLogKeyword.CloudDownload, "Download of read-only world {WorldId} goes to its writable copy", world.WorldId);
            return await DownloadLatestAsync(copy, cancellationToken);
        }

        logger.Information(AppLogKeyword.CloudDownload, "Download of read-only world {WorldId} goes into profile {ProfileId} as a new world", world.WorldId, profile.RootId);
        return await DownloadCloudWorldAsync(profile, world.WorldId, cancellationToken);
    }

    // The copy of a read-only world in a folder SaveHarbor may write to, when there is one.
    private async Task<GameWorld?> FindWritableCopyAsync(GameWorld world, CancellationToken cancellationToken)
    {
        if (!world.IsReadOnly)
        {
            return null;
        }

        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        foreach (var root in (await adapter.DiscoverSaveRootsAsync(cancellationToken)).Where(root => root.CanReceiveWorlds))
        {
            string path;
            try
            {
                path = adapter.GetExpectedWorldPath(root, world.WorldId);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            if (File.Exists(path)
                && await adapter.ReadWorldAsync(path, cancellationToken) is { IsReadOnly: false } copy
                && string.Equals(copy.WorldId, world.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                return copy;
            }
        }

        return null;
    }

    private async Task<CloudSyncResult?> RefuseReadOnlyWithWritableCopyAsync(GameWorld world, AppLogKeyword keyword, CancellationToken cancellationToken)
    {
        if (await FindWritableCopyAsync(world, cancellationToken) is null)
        {
            return null;
        }

        logger.Warning(keyword, "Refused read-only copy of world {WorldId}: a writable copy exists", world.WorldId);
        return new CloudSyncResult(false, CloudSyncState.Error, UseWritableCopyMessage);
    }
}
