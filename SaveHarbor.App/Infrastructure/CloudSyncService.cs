using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using System.IO;
using System.Security.Cryptography;

namespace SaveHarbor.App.Infrastructure;

public sealed class CloudSyncService : ICloudSyncService
{
    private readonly ICloudProvider cloudProvider;
    private readonly ILocalSyncStateService localSyncStateService;
    private readonly IBackupService backupService;

    public CloudSyncService(
        ICloudProvider cloudProvider,
        ILocalSyncStateService localSyncStateService,
        IBackupService backupService)
    {
        this.cloudProvider = cloudProvider;
        this.localSyncStateService = localSyncStateService;
        this.backupService = backupService;
    }

    public async Task<CloudSyncStatus> RefreshStatusAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        localState.LastCloudCheckAtUtc = DateTimeOffset.UtcNow;

        var connection = await cloudProvider.GetConnectionStatusAsync(cancellationToken);
        if (!connection.IsConnected)
        {
            await localSyncStateService.SaveAsync(localState, cancellationToken);
            return new CloudSyncStatus(
                CloudSyncState.NotConnected,
                connection,
                null,
                null,
                null,
                localState,
                "Cloud not connected",
                "Connect a cloud provider before syncing this world.");
        }

        var manifest = await cloudProvider.GetWorldManifestAsync(world.WorldId, cancellationToken);
        var sessionLock = await cloudProvider.GetSessionLockAsync(world.WorldId, cancellationToken);
        var latestVersion = manifest?.LatestVersion;

        if (latestVersion is not null)
        {
            localState.LastKnownCloudVersionNumber = latestVersion.VersionNumber;
            localState.LastKnownCloudVersionId = latestVersion.VersionId;
        }

        await localSyncStateService.SaveAsync(localState, cancellationToken);

        if (manifest is null || latestVersion is null)
        {
            return new CloudSyncStatus(
                CloudSyncState.ConnectedNoCloudSave,
                connection,
                manifest,
                null,
                sessionLock,
                localState,
                "No cloud save",
                "This world has no shared cloud version yet. Upload current to create v1.");
        }

        if (IsActiveOtherPlayerLock(sessionLock))
        {
            return new CloudSyncStatus(
                CloudSyncState.SomeonePlaying,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Someone playing",
                $"{sessionLock!.PlayerName} is playing from cloud v{sessionLock.BasedOnVersionNumber}.");
        }

        if (localState.LocalBaseVersionNumber is null)
        {
            return new CloudSyncStatus(
                CloudSyncState.CloudNewer,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Download needed",
                $"Cloud has v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}. This local world has no cloud base version yet.");
        }

        if (localState.LocalBaseVersionNumber < latestVersion.VersionNumber)
        {
            return new CloudSyncStatus(
                CloudSyncState.CloudNewer,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Cloud is newer",
                $"Cloud has v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}. Local is based on v{localState.LocalBaseVersionNumber}.");
        }

        if (localState.LocalBaseVersionNumber > latestVersion.VersionNumber)
        {
            return new CloudSyncStatus(
                CloudSyncState.Conflict,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Sync conflict",
                $"Local is based on v{localState.LocalBaseVersionNumber}, but cloud latest is v{latestVersion.VersionNumber}. Review before syncing.");
        }

        return new CloudSyncStatus(
            CloudSyncState.UpToDate,
            connection,
            manifest,
            latestVersion,
            sessionLock,
            localState,
            "In sync",
            $"Local world is based on the latest cloud version: v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}.");
    }

    public async Task<CloudSyncResult> DownloadLatestAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is null)
        {
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
                return new CloudSyncResult(false, status.State, download.Message);
            }

            var archiveSha256 = await ComputeFileSha256Async(download.ArchivePath, cancellationToken);
            if (!string.Equals(archiveSha256, status.LatestVersion.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
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
        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is not null &&
            status.LocalState.LocalBaseVersionNumber != status.LatestVersion.VersionNumber)
        {
            return new CloudSyncResult(
                false,
                CloudSyncState.Conflict,
                $"Upload blocked. Cloud is v{status.LatestVersion.VersionNumber}, but this local save is based on {(status.LocalState.LocalBaseVersionNumber is null ? "no cloud version" : $"v{status.LocalState.LocalBaseVersionNumber}")}.");
        }

        var backup = await backupService.CreateBackupAsync(world, "cloud-upload", cancellationToken);
        var archiveSha256 = await ComputeFileSha256Async(backup.FilePath, cancellationToken);
        var nextVersionNumber = (status.LatestVersion?.VersionNumber ?? 0) + 1;
        var safePlayer = MakeSafeFileName(Environment.UserName);
        var versionId = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safePlayer}_v{nextVersionNumber}";
        var archiveFileName = $"{versionId}.zip";

        var version = new CloudVersionMetadata
        {
            VersionNumber = nextVersionNumber,
            VersionId = versionId,
            UploadedAtUtc = DateTimeOffset.UtcNow,
            UploadedBy = Environment.UserName,
            UploaderMachine = Environment.MachineName,
            ArchiveFileName = archiveFileName,
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

        return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Uploaded {world.WorldName} v{version.VersionNumber}.");
    }

    public async Task<CloudSyncResult> StartSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is null)
        {
            return new CloudSyncResult(false, status.State, "Upload this world first before starting a shared play session.");
        }

        if (status.LocalState.LocalBaseVersionNumber != status.LatestVersion.VersionNumber)
        {
            return new CloudSyncResult(false, CloudSyncState.CloudNewer, "Download the latest cloud save before starting a shared play session.");
        }

        if (IsActiveOtherPlayerLock(status.SessionLock))
        {
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{status.SessionLock!.PlayerName} already appears to be playing.");
        }

        if (IsActiveOwnLock(status.SessionLock))
        {
            return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, $"Session is already active for {world.WorldName} from v{status.SessionLock!.BasedOnVersionNumber}.");
        }

        var now = DateTimeOffset.UtcNow;
        var sessionLock = new CloudSessionLock
        {
            LockId = Guid.NewGuid().ToString("D"),
            WorldId = world.WorldId,
            PlayerName = Environment.UserName,
            MachineName = Environment.MachineName,
            StartedAtUtc = now,
            LastHeartbeatAtUtc = now,
            BasedOnVersionNumber = status.LatestVersion.VersionNumber,
            BasedOnVersionId = status.LatestVersion.VersionId,
            ExpiresAtUtc = now.AddHours(6),
            Status = "Playing"
        };

        await cloudProvider.WriteSessionLockAsync(sessionLock, cancellationToken);
        return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, $"Session started for {world.WorldName} from v{status.LatestVersion.VersionNumber}.");
    }

    public async Task<CloudSyncResult> EndSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.SessionLock is null)
        {
            return new CloudSyncResult(true, status.State, "No active session lock exists.");
        }

        if (!IsOwnLock(status.SessionLock))
        {
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{status.SessionLock.PlayerName} owns the active session lock.");
        }

        await cloudProvider.ClearSessionLockAsync(world.WorldId, status.SessionLock.LockId, cancellationToken);
        return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Session ended for {world.WorldName}.");
    }

    private static bool IsActiveOtherPlayerLock(CloudSessionLock? sessionLock)
    {
        if (sessionLock is null)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow > sessionLock.ExpiresAtUtc)
        {
            return false;
        }

        return !string.Equals(sessionLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnLock(CloudSessionLock? sessionLock)
    {
        return sessionLock is not null &&
            string.Equals(sessionLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveOwnLock(CloudSessionLock? sessionLock)
    {
        return IsOwnLock(sessionLock) && DateTimeOffset.UtcNow <= sessionLock!.ExpiresAtUtc;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", value.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
