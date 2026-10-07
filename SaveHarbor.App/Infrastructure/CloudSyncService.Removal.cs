using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    // The latest shared version is first downloaded, hash-checked and kept as a local backup ("cloud-removed"); only
    // then is the world removed from the shared folder. Never while someone else is playing it.
    public async Task<CloudSyncResult> RemoveCloudWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        if (!SafePath.IsSafeSegment(worldId))
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud world has an invalid id. Nothing was removed.");
        }

        var connection = await cloudProvider.GetConnectionStatusAsync(game, cancellationToken);
        if (!connection.IsConnected)
        {
            return new CloudSyncResult(false, CloudSyncState.NotConnected, "Cloud sync is not connected.");
        }

        var manifest = await cloudProvider.GetWorldManifestAsync(game, worldId, cancellationToken);
        if (manifest is null)
        {
            return new CloudSyncResult(false, CloudSyncState.ConnectedNoCloudSave, "This world is not in the cloud.");
        }

        var sessionLock = await cloudProvider.GetSessionLockAsync(game, worldId, cancellationToken);
        if (IsActiveOtherPlayerLock(sessionLock))
        {
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{sessionLock!.PlayerName} is playing this world. It cannot be removed until they finish.");
        }

        var worldName = string.IsNullOrWhiteSpace(manifest.WorldName) ? worldId : manifest.WorldName;
        string? backupName = null;
        if (manifest.LatestVersion is { } latest)
        {
            var kept = await KeepLatestVersionAsync(game, worldId, worldName, latest, cancellationToken);
            if (!kept.IsSuccess)
            {
                return kept;
            }

            backupName = kept.Message;
        }

        var removal = await cloudProvider.RemoveWorldAsync(game, worldId, cancellationToken);
        if (!removal.IsSuccess)
        {
            logger.Warning(AppLogKeyword.CloudSync, "Cloud removal of world {WorldId} failed: {Message}", worldId, removal.Message);
            return new CloudSyncResult(false, CloudSyncState.Error, backupName is null ? removal.Message : $"{removal.Message} The latest version was still saved as backup {backupName}.");
        }

        logger.Information(AppLogKeyword.CloudSync, "Removed world {WorldId} from the cloud", worldId);
        return new CloudSyncResult(
            true,
            CloudSyncState.ConnectedNoCloudSave,
            backupName is null
                ? $"Removed {worldName} from the cloud. {removal.Message}"
                : $"Removed {worldName} from the cloud. {removal.Message} The latest version was saved as backup {backupName}.");
    }

    // On success the message is the backup's file name.
    private async Task<CloudSyncResult> KeepLatestVersionAsync(GameId game, string worldId, string worldName, CloudVersionMetadata latest, CancellationToken cancellationToken)
    {
        if (!SafePath.IsSafeSegment(latest.ArchiveFileName))
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud version has an invalid archive name. Nothing was removed.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "SaveHarbor", "cloud-downloads", $"{Guid.NewGuid():N}_{latest.ArchiveFileName}");
        var world = new GameWorld(game, worldId, worldName, string.Empty, string.Empty, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);
        try
        {
            var download = await cloudProvider.DownloadVersionAsync(new CloudDownloadRequest(world, latest, tempPath), cancellationToken);
            if (!download.IsSuccess || download.ArchivePath is null)
            {
                return new CloudSyncResult(false, CloudSyncState.Error, $"The latest version could not be downloaded as a backup, so nothing was removed. {download.Message}");
            }

            var archiveSha256 = await FileHashCalculator.ComputeSha256Async(download.ArchivePath, cancellationToken);
            if (!string.Equals(archiveSha256, latest.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new CloudSyncResult(false, CloudSyncState.Error, "The downloaded backup did not match the cloud manifest, so nothing was removed.");
            }

            var backup = await backupService.StoreArchiveCopyAsync(download.ArchivePath, game, BackupReasons.CloudRemoved, cancellationToken);
            return new CloudSyncResult(true, CloudSyncState.ConnectedNoCloudSave, backup.FileName);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
