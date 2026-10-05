using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService : ICloudSyncService
{
    private readonly ICloudProvider cloudProvider;
    private readonly ILocalSyncStateService localSyncStateService;
    private readonly IBackupService backupService;
    private readonly IGameRegistry gameRegistry;
    private readonly IPlayerIdentity playerIdentity;
    private readonly IAppSettingsStore settings;
    private readonly IAppLogger logger;

    public CloudSyncService(
        ICloudProvider cloudProvider,
        ILocalSyncStateService localSyncStateService,
        IBackupService backupService,
        IGameRegistry gameRegistry,
        IPlayerIdentity playerIdentity,
        IAppSettingsStore settings,
        IAppLogger logger)
    {
        this.cloudProvider = cloudProvider;
        this.localSyncStateService = localSyncStateService;
        this.backupService = backupService;
        this.gameRegistry = gameRegistry;
        this.playerIdentity = playerIdentity;
        this.settings = settings;
        this.logger = logger;
    }

    public async Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudProvider, "Connecting cloud provider {ProviderName}", cloudProvider.ProviderName);
        return await cloudProvider.ConnectAsync(game, cancellationToken);
    }

    public async Task<CloudSyncStatus> RefreshStatusAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudSync, "Refreshing cloud status for world {WorldId} ({WorldName})", world.WorldId, world.WorldName);

        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        localState.LastCloudCheckAtUtc = DateTimeOffset.UtcNow;

        var connection = await cloudProvider.GetConnectionStatusAsync(world.Game, cancellationToken);
        if (!connection.IsConnected)
        {
            await localSyncStateService.SaveAsync(localState, cancellationToken);
            logger.Warning(AppLogKeyword.CloudSync, "Cloud provider is not connected for world {WorldId}", world.WorldId);
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

        var manifest = await cloudProvider.GetWorldManifestAsync(world.Game, world.WorldId, cancellationToken);
        var sessionLock = await cloudProvider.GetSessionLockAsync(world.Game, world.WorldId, cancellationToken);
        var latestVersion = manifest?.LatestVersion;

        if (latestVersion is not null)
        {
            localState.LastKnownCloudVersionNumber = latestVersion.VersionNumber;
            localState.LastKnownCloudVersionId = latestVersion.VersionId;
        }

        await localSyncStateService.SaveAsync(localState, cancellationToken);

        if (manifest is null || latestVersion is null)
        {
            logger.Debug(AppLogKeyword.CloudSync, "No cloud save exists for world {WorldId}", world.WorldId);
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
            logger.Warning(
                AppLogKeyword.CloudSession,
                "Active session lock by {PlayerName} for world {WorldId}",
                sessionLock!.PlayerName,
                world.WorldId);

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
            logger.Debug(AppLogKeyword.CloudSync, "Cloud is newer because local world {WorldId} has no base version", world.WorldId);
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

        var hasLocalChanges = await DetectLocalChangesAsync(localState, world, cancellationToken) == true;

        if (localState.LocalBaseVersionNumber < latestVersion.VersionNumber)
        {
            logger.Debug(
                AppLogKeyword.CloudSync,
                "Cloud is newer for world {WorldId}. LocalBase={LocalBaseVersion} Latest={LatestVersion} LocalChanges={LocalChanges}",
                world.WorldId,
                localState.LocalBaseVersionNumber,
                latestVersion.VersionNumber,
                hasLocalChanges);

            return hasLocalChanges
                ? new CloudSyncStatus(
                    CloudSyncState.Conflict,
                    connection,
                    manifest,
                    latestVersion,
                    sessionLock,
                    localState,
                    "Both changed",
                    $"Cloud has v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}, and you have played since v{localState.LocalBaseVersionNumber}. Downloading replaces your local progress (it is backed up first).")
                { HasLocalChanges = true }
                : new CloudSyncStatus(
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
            logger.Warning(
                AppLogKeyword.CloudSync,
                "Cloud sync conflict for world {WorldId}. LocalBase={LocalBaseVersion} Latest={LatestVersion}",
                world.WorldId,
                localState.LocalBaseVersionNumber,
                latestVersion.VersionNumber);

            return new CloudSyncStatus(
                CloudSyncState.Conflict,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Sync conflict",
                $"Local is based on v{localState.LocalBaseVersionNumber}, but cloud latest is v{latestVersion.VersionNumber}. Review before syncing.")
            { HasLocalChanges = hasLocalChanges };
        }

        if (hasLocalChanges)
        {
            logger.Debug(AppLogKeyword.CloudSync, "World {WorldId} has local progress since version {VersionNumber}", world.WorldId, latestVersion.VersionNumber);
            return new CloudSyncStatus(
                CloudSyncState.LocalNewerUploadSafe,
                connection,
                manifest,
                latestVersion,
                sessionLock,
                localState,
                "Not shared yet",
                $"You have played since v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}. Upload to share your progress with the group.")
            { HasLocalChanges = true };
        }

        logger.Debug(AppLogKeyword.CloudSync, "World {WorldId} is in sync at version {VersionNumber}", world.WorldId, latestVersion.VersionNumber);
        return new CloudSyncStatus(
            CloudSyncState.UpToDate,
            connection,
            manifest,
            latestVersion,
            sessionLock,
            localState,
            "In sync",
            $"Your world matches the latest cloud version: v{latestVersion.VersionNumber} by {latestVersion.UploadedBy}.");
    }

    // A state saved before fingerprints existed gets one the first time its world is found unchanged, so later checks
    // compare file contents instead of write times.
    private async Task<bool?> DetectLocalChangesAsync(LocalSyncState localState, GameWorld world, CancellationToken cancellationToken)
    {
        if (!File.Exists(world.SavePath) && !Directory.Exists(world.SavePath))
        {
            return null;
        }

        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        var changed = await LocalChangeDetector.HasChangedAsync(localState, world, adapter, cancellationToken);
        if (changed == false && string.IsNullOrEmpty(localState.LocalBaseContentHash))
        {
            await LocalChangeDetector.CaptureAsync(localState, world, adapter, cancellationToken);
            await localSyncStateService.SaveAsync(localState, cancellationToken);
        }

        return changed;
    }
}
