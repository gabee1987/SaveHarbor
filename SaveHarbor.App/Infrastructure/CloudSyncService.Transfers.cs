using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    public async Task<CloudSyncResult> DownloadLatestAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudDownload, "Starting cloud download for world {WorldId}", world.WorldId);

        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            logger.Warning(AppLogKeyword.CloudDownload, "Cloud download blocked because provider is not connected for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is null)
        {
            logger.Warning(AppLogKeyword.CloudDownload, "Cloud download blocked because no cloud save exists for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "No cloud save is available for this world.");
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "SaveHarbor",
            "cloud-downloads",
            $"{Guid.NewGuid():N}_{status.LatestVersion.ArchiveFileName}");

        try
        {
            var download = await cloudProvider.DownloadVersionAsync(
                new CloudDownloadRequest(world, status.LatestVersion, tempPath),
                cancellationToken);

            if (!download.IsSuccess || download.ArchivePath is null)
            {
                logger.Warning(AppLogKeyword.CloudDownload, "Cloud download failed for world {WorldId}: {Message}", world.WorldId, download.Message);
                return new CloudSyncResult(false, status.State, download.Message);
            }

            var archiveSha256 = await FileHashCalculator.ComputeSha256Async(download.ArchivePath, cancellationToken);
            if (!string.Equals(archiveSha256, status.LatestVersion.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning(
                    AppLogKeyword.CloudDownload,
                    "Cloud download hash mismatch for world {WorldId}. Expected={ExpectedHash} Actual={ActualHash}",
                    world.WorldId,
                    status.LatestVersion.ArchiveSha256,
                    archiveSha256);

                return new CloudSyncResult(false, CloudSyncState.Error, "Downloaded archive hash did not match the cloud manifest. Local save was not changed.");
            }

            await backupService.RestoreBackupAsync(download.ArchivePath, world, cancellationToken);

            var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
            localState.LastKnownCloudVersionNumber = status.LatestVersion.VersionNumber;
            localState.LastKnownCloudVersionId = status.LatestVersion.VersionId;
            localState.LocalBaseVersionNumber = status.LatestVersion.VersionNumber;
            localState.LocalBaseVersionId = status.LatestVersion.VersionId;
            localState.LastDownloadedAtUtc = DateTimeOffset.UtcNow;
            await localSyncStateService.SaveAsync(localState, cancellationToken);

            logger.Information(AppLogKeyword.CloudDownload, "Completed cloud download for world {WorldId} version {VersionNumber}", world.WorldId, status.LatestVersion.VersionNumber);
            return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Downloaded {world.WorldName} v{status.LatestVersion.VersionNumber}. Local backup was created first.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<CloudSyncResult> UploadCurrentAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudUpload, "Starting cloud upload for world {WorldId}", world.WorldId);

        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            logger.Warning(AppLogKeyword.CloudUpload, "Cloud upload blocked because provider is not connected for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is not null &&
            status.LocalState.LocalBaseVersionNumber != status.LatestVersion.VersionNumber)
        {
            logger.Warning(
                AppLogKeyword.CloudUpload,
                "Cloud upload blocked by conflict for world {WorldId}. LocalBase={LocalBaseVersion} Latest={LatestVersion}",
                world.WorldId,
                status.LocalState.LocalBaseVersionNumber,
                status.LatestVersion.VersionNumber);

            return new CloudSyncResult(
                false,
                CloudSyncState.Conflict,
                $"Upload blocked. Cloud is v{status.LatestVersion.VersionNumber}, but this local save is based on {(status.LocalState.LocalBaseVersionNumber is null ? "no cloud version" : $"v{status.LocalState.LocalBaseVersionNumber}")}.");
        }

        var backup = await backupService.CreateBackupAsync(world, "cloud-upload", cancellationToken);
        var archiveSha256 = await FileHashCalculator.ComputeSha256Async(backup.FilePath, cancellationToken);
        var nextVersionNumber = (status.LatestVersion?.VersionNumber ?? 0) + 1;
        var safePlayer = FileNameSanitizer.MakeSafeFileName(Environment.UserName);
        var versionId = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safePlayer}_v{nextVersionNumber}";

        var version = new CloudVersionMetadata
        {
            VersionNumber = nextVersionNumber,
            VersionId = versionId,
            UploadedAtUtc = DateTimeOffset.UtcNow,
            UploadedBy = Environment.UserName,
            UploaderMachine = Environment.MachineName,
            ArchiveFileName = $"{versionId}.zip",
            ArchiveSha256 = archiveSha256,
            ArchiveSizeBytes = new FileInfo(backup.FilePath).Length,
            SourceWorldModifiedAtUtc = world.LastModifiedAt.ToUniversalTime(),
            BasedOnVersionNumber = status.LatestVersion?.VersionNumber ?? 0,
            BasedOnVersionId = status.LatestVersion?.VersionId ?? string.Empty
        };

        var upload = await cloudProvider.UploadVersionAsync(
            new CloudUploadRequest(world, backup.FilePath, version, status.Manifest),
            cancellationToken);

        if (!upload.IsSuccess)
        {
            logger.Warning(AppLogKeyword.CloudUpload, "Cloud upload failed for world {WorldId}: {Message}", world.WorldId, upload.Message);
            return new CloudSyncResult(false, CloudSyncState.Error, upload.Message);
        }

        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        localState.LastKnownCloudVersionNumber = version.VersionNumber;
        localState.LastKnownCloudVersionId = version.VersionId;
        localState.LocalBaseVersionNumber = version.VersionNumber;
        localState.LocalBaseVersionId = version.VersionId;
        localState.LastUploadedAtUtc = DateTimeOffset.UtcNow;
        localState.LastLocalBackupPath = backup.FilePath;
        await localSyncStateService.SaveAsync(localState, cancellationToken);

        if (IsOwnLock(status.SessionLock))
        {
            await cloudProvider.ClearSessionLockAsync(world.WorldId, status.SessionLock!.LockId, cancellationToken);
        }

        logger.Information(AppLogKeyword.CloudUpload, "Completed cloud upload for world {WorldId} version {VersionNumber}", world.WorldId, version.VersionNumber);
        return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Uploaded {world.WorldName} v{version.VersionNumber}.");
    }
}
