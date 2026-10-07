using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

// Downloads of shared worlds that do not exist on this PC yet. They never overwrite a local world.
public sealed partial class CloudSyncService
{
    public async Task<CloudSyncResult> DownloadLatestAvailableAsync(GameSaveRoot profile, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudDownload, "Starting cloud download into profile {ProfileId}", profile.RootId);

        var connection = await cloudProvider.GetConnectionStatusAsync(profile.Game, cancellationToken);
        if (!connection.IsConnected)
        {
            logger.Warning(AppLogKeyword.CloudDownload, "Cloud download blocked because provider is not connected for profile {ProfileId}", profile.RootId);
            return new CloudSyncResult(false, CloudSyncState.NotConnected, "Cloud sync is not connected.");
        }

        var manifests = await cloudProvider.ListWorldManifestsAsync(profile.Game, cancellationToken);
        var manifest = manifests
            .Where(candidate => candidate.LatestVersion is not null)
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .ThenByDescending(candidate => candidate.LatestVersion!.UploadedAtUtc)
            .FirstOrDefault();

        if (manifest?.LatestVersion is null)
        {
            return new CloudSyncResult(false, CloudSyncState.ConnectedNoCloudSave, "No cloud save is available to download.");
        }

        return await DownloadAsNewWorldAsync(profile, manifest, cancellationToken);
    }

    public async Task<CloudSyncResult> DownloadCloudWorldAsync(GameSaveRoot profile, string worldId, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudDownload, "Starting download of cloud world {WorldId} into profile {ProfileId}", worldId, profile.RootId);

        if (!SafePath.IsSafeSegment(worldId))
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud world has an invalid id. Nothing was changed.");
        }

        var connection = await cloudProvider.GetConnectionStatusAsync(profile.Game, cancellationToken);
        if (!connection.IsConnected)
        {
            return new CloudSyncResult(false, CloudSyncState.NotConnected, "Cloud sync is not connected.");
        }

        var manifest = await cloudProvider.GetWorldManifestAsync(profile.Game, worldId, cancellationToken);
        if (manifest?.LatestVersion is null)
        {
            return new CloudSyncResult(false, CloudSyncState.ConnectedNoCloudSave, "This world has no shared version to download.");
        }

        if (!string.Equals(manifest.WorldId, worldId, StringComparison.OrdinalIgnoreCase))
        {
            logger.Warning(AppLogKeyword.CloudDownload, "Cloud manifest in folder {WorldId} names another world", worldId);
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud manifest does not match this world. Nothing was changed.");
        }

        return await DownloadAsNewWorldAsync(profile, manifest, cancellationToken);
    }

    private async Task<CloudSyncResult> DownloadAsNewWorldAsync(GameSaveRoot profile, CloudWorldManifest manifest, CancellationToken cancellationToken)
    {
        var latest = manifest.LatestVersion!;
        string targetWorldPath;
        try
        {
            targetWorldPath = gameRegistry.Get(profile.Game).SaveAdapter.GetExpectedWorldPath(profile, manifest.WorldId);
        }
        catch (InvalidDataException)
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud manifest contains an invalid world id. Nothing was changed.");
        }

        if (!SafePath.IsSafeSegment(latest.ArchiveFileName))
        {
            return new CloudSyncResult(false, CloudSyncState.Error, "The cloud version has an invalid archive name. Nothing was changed.");
        }

        if (File.Exists(targetWorldPath) || Directory.Exists(targetWorldPath))
        {
            return new CloudSyncResult(false, CloudSyncState.Conflict, $"A local world file or folder already exists for {manifest.WorldName}. Refresh worlds and use normal Download.");
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "SaveHarbor",
            "cloud-downloads",
            $"{Guid.NewGuid():N}_{latest.ArchiveFileName}");

        var world = new GameWorld(
            profile.Game,
            manifest.WorldId,
            string.IsNullOrWhiteSpace(manifest.WorldName) ? manifest.WorldId : manifest.WorldName,
            "Unknown",
            targetWorldPath,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            0,
            0);

        try
        {
            var download = await cloudProvider.DownloadVersionAsync(
                new CloudDownloadRequest(world, latest, tempPath),
                cancellationToken);

            if (!download.IsSuccess || download.ArchivePath is null)
            {
                logger.Warning(AppLogKeyword.CloudDownload, "Cloud download failed for world {WorldId}: {Message}", manifest.WorldId, download.Message);
                return new CloudSyncResult(false, CloudSyncState.Error, download.Message);
            }

            var archiveSha256 = await FileHashCalculator.ComputeSha256Async(download.ArchivePath, cancellationToken);
            if (!string.Equals(archiveSha256, latest.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning(
                    AppLogKeyword.CloudDownload,
                    "Cloud download hash mismatch for world {WorldId}. Expected={ExpectedHash} Actual={ActualHash}",
                    manifest.WorldId,
                    latest.ArchiveSha256,
                    archiveSha256);

                return new CloudSyncResult(false, CloudSyncState.Error, "Downloaded archive hash did not match the cloud manifest. Local save was not changed.");
            }

            var importedPath = await backupService.ImportBackupAsNewWorldAsync(download.ArchivePath, profile, overwriteExisting: false, cancellationToken);
            var importedWorld = world with { SavePath = importedPath };
            var localState = await localSyncStateService.LoadAsync(importedWorld, cancellationToken);
            localState.LastKnownCloudVersionNumber = latest.VersionNumber;
            localState.LastKnownCloudVersionId = latest.VersionId;
            localState.LocalBaseVersionNumber = latest.VersionNumber;
            localState.LocalBaseVersionId = latest.VersionId;
            localState.LastDownloadedAtUtc = DateTimeOffset.UtcNow;
            await LocalChangeDetector.CaptureAsync(localState, importedWorld, gameRegistry.Get(profile.Game).SaveAdapter, cancellationToken);
            await localSyncStateService.SaveAsync(localState, cancellationToken);

            logger.Information(AppLogKeyword.CloudDownload, "Completed cloud download for world {WorldId} version {VersionNumber}", manifest.WorldId, latest.VersionNumber);
            return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Downloaded {world.WorldName} v{latest.VersionNumber} to this PC.");
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
