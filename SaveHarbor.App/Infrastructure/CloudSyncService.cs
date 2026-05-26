using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class CloudSyncService : ICloudSyncService
{
    private readonly ICloudProvider cloudProvider;
    private readonly ILocalSyncStateService localSyncStateService;

    public CloudSyncService(ICloudProvider cloudProvider, ILocalSyncStateService localSyncStateService)
    {
        this.cloudProvider = cloudProvider;
        this.localSyncStateService = localSyncStateService;
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
                "Connect Google Drive when the provider is implemented.");
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
                "Upload this world to create the first shared cloud save.");
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
                $"{sessionLock!.PlayerName} is playing",
                $"Started from v{sessionLock.BasedOnVersionNumber}.");
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
                "Cloud save available",
                $"Latest cloud save is v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}.");
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
                $"Latest cloud save is v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}.");
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
                "Cloud conflict",
                "Local sync state is ahead of cloud. Check cloud state before continuing.");
        }

        return new CloudSyncStatus(
            CloudSyncState.UpToDate,
            connection,
            manifest,
            latestVersion,
            sessionLock,
            localState,
            "Cloud up to date",
            $"Local save is based on v{latestVersion.VersionNumber}.");
    }

    public Task<CloudSyncResult> DownloadLatestAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudSyncResult(
            false,
            CloudSyncState.NotConnected,
            "Download latest will be enabled after the Google Drive provider is implemented."));
    }

    public Task<CloudSyncResult> UploadCurrentAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudSyncResult(
            false,
            CloudSyncState.NotConnected,
            "Upload current will be enabled after the Google Drive provider is implemented."));
    }

    public Task<CloudSyncResult> StartSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudSyncResult(
            false,
            CloudSyncState.NotConnected,
            "Session locks will be enabled after the Google Drive provider is implemented."));
    }

    public Task<CloudSyncResult> EndSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CloudSyncResult(
            false,
            CloudSyncState.NotConnected,
            "Session locks will be enabled after the Google Drive provider is implemented."));
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
}
