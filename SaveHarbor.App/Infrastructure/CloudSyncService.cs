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

        var otherPlayerPlaying = IsActiveOtherPlayerLock(sessionLock);
        var hasLocalChanges = SyncStateClassifier.NeedsLocalChangeCheck(localState.LocalBaseVersionNumber, latestVersion?.VersionNumber, otherPlayerPlaying)
            && await DetectLocalChangesAsync(localState, world, cancellationToken) == true;
        var state = SyncStateClassifier.Classify(localState.LocalBaseVersionNumber, latestVersion?.VersionNumber, hasLocalChanges, otherPlayerPlaying);

        logger.Debug(
            AppLogKeyword.CloudSync,
            "Cloud status for world {WorldId}: {State}. LocalBase={LocalBaseVersion} Latest={LatestVersion} LocalChanges={LocalChanges}",
            world.WorldId,
            state,
            localState.LocalBaseVersionNumber,
            latestVersion?.VersionNumber,
            hasLocalChanges);

        var (title, detail) = DescribeStatus(state, localState.LocalBaseVersionNumber, latestVersion, sessionLock);
        return new CloudSyncStatus(state, connection, manifest, latestVersion, sessionLock, localState, title, detail)
        {
            HasLocalChanges = hasLocalChanges
        };
    }

    private static (string Title, string Detail) DescribeStatus(
        CloudSyncState state,
        int? localBase,
        CloudVersionMetadata? latest,
        CloudSessionLock? sessionLock) => state switch
    {
        CloudSyncState.ConnectedNoCloudSave =>
            ("No cloud save", "This world has no shared cloud version yet. Upload current to create v1."),
        CloudSyncState.SomeonePlaying =>
            ("Someone playing", $"{sessionLock!.PlayerName} is playing from cloud v{sessionLock.BasedOnVersionNumber}."),
        CloudSyncState.CloudNewer when localBase is null =>
            ("Download needed", $"Cloud has v{latest!.VersionNumber} by {latest.UploadedBy}. This local world has no cloud base version yet."),
        CloudSyncState.CloudNewer =>
            ("Cloud is newer", $"Cloud has v{latest!.VersionNumber} by {latest.UploadedBy}. Local is based on v{localBase}."),
        CloudSyncState.Conflict when localBase > latest!.VersionNumber =>
            ("Sync conflict", $"Local is based on v{localBase}, but cloud latest is v{latest.VersionNumber}. Review before syncing."),
        CloudSyncState.Conflict =>
            ("Both changed", $"Cloud has v{latest!.VersionNumber} by {latest.UploadedBy}, and you have played since v{localBase}. Downloading replaces your local progress (it is backed up first)."),
        CloudSyncState.LocalNewerUploadSafe =>
            ("Not shared yet", $"You have played since v{latest!.VersionNumber} by {latest.UploadedBy}. Upload to share your progress with the group."),
        _ =>
            ("In sync", $"Your world matches the latest cloud version: v{latest!.VersionNumber} by {latest.UploadedBy}.")
    };

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
