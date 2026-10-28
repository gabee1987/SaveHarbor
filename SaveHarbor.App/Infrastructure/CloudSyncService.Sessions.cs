using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    public async Task<CloudSyncResult> StartSessionAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudSession, "Starting cloud session for world {WorldId}", world.WorldId);
        if (await RefuseReadOnlyWithWritableCopyAsync(world, AppLogKeyword.CloudSession, cancellationToken) is { } refused)
        {
            return refused;
        }

        var status = await RefreshStatusAsync(world, cancellationToken);
        if (status.State == CloudSyncState.UpdateRequired)
        {
            return UpdateRequiredResult(status, AppLogKeyword.CloudSession, world);
        }

        if (!status.Connection.IsConnected)
        {
            logger.Warning(AppLogKeyword.CloudSession, "Start session blocked because provider is not connected for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.LatestVersion is null)
        {
            logger.Warning(AppLogKeyword.CloudSession, "Start session blocked because no cloud save exists for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "Upload this world first before starting a shared play session.");
        }

        if (status.LocalState.LocalBaseVersionNumber != status.LatestVersion.VersionNumber)
        {
            logger.Warning(AppLogKeyword.CloudSession, "Start session blocked because local world {WorldId} is not on latest cloud version", world.WorldId);
            return new CloudSyncResult(false, CloudSyncState.CloudNewer, "Download the latest cloud save before starting a shared play session.");
        }

        var now = Now;
        if (IsActiveOtherPlayerLock(status.SessionLock, now))
        {
            logger.Warning(AppLogKeyword.CloudSession, "Start session blocked by active lock from {PlayerName} for world {WorldId}", status.SessionLock!.PlayerName, world.WorldId);
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{status.SessionLock!.PlayerName} already appears to be playing.");
        }

        if (IsActiveOwnLock(status.SessionLock, now))
        {
            return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, $"Session is already active for {world.WorldName} from v{status.SessionLock!.BasedOnVersionNumber}.");
        }

        var requested = new CloudSessionLock
        {
            LockId = Guid.NewGuid().ToString("D"),
            WorldId = world.WorldId,
            PlayerName = playerIdentity.DisplayName,
            MachineName = Environment.MachineName,
            DeviceId = deviceIdentity.DeviceId,
            StartedAtUtc = now,
            LastHeartbeatAtUtc = now,
            BasedOnVersionNumber = status.LatestVersion.VersionNumber,
            BasedOnVersionId = status.LatestVersion.VersionId,
            ExpiresAtUtc = now + LockTtl,
            Status = "Playing"
        };

        var attempt = await cloudProvider.TryAcquireSessionLockAsync(world.Game, requested, cancellationToken);
        if (!attempt.Acquired)
        {
            if (IsOwnLock(attempt.Current))
            {
                logger.Warning(AppLogKeyword.CloudSession, "Session lock for world {WorldId} could not be confirmed", world.WorldId);
                return new CloudSyncResult(false, CloudSyncState.Error, "The session could not be confirmed. Try again.");
            }

            logger.Warning(AppLogKeyword.CloudSession, "Session lock for world {WorldId} went to {PlayerName}", world.WorldId, attempt.Current.PlayerName);
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{attempt.Current.PlayerName} started playing first.");
        }

        lastHeartbeats[HeartbeatKey(world)] = now;
        var localState = await localSyncStateService.LoadAsync(world, cancellationToken);
        localState.PendingUploadSinceUtc ??= now;
        await localSyncStateService.SaveAsync(localState, cancellationToken);
        logger.Information(AppLogKeyword.CloudSession, "Cloud session started for world {WorldId} from version {VersionNumber}", world.WorldId, attempt.Current.BasedOnVersionNumber);
        return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, $"Session started for {world.WorldName} from v{attempt.Current.BasedOnVersionNumber}.");
    }

    // Called repeatedly while the game runs; renews this device's lock once per heartbeat interval and is a no-op in
    // between, so it costs nothing to call often. The attempt is recorded first, so a failing network is retried at the
    // next interval rather than on every call.
    public async Task<CloudSyncResult> HeartbeatSessionAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var key = HeartbeatKey(world);
        var now = Now;
        if (lastHeartbeats.TryGetValue(key, out var last) && now - last < HeartbeatInterval)
        {
            return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, "Session lock is current.");
        }

        lastHeartbeats[key] = now;

        var current = await cloudProvider.GetSessionLockAsync(world.Game, world.WorldId, cancellationToken);
        var renewed = IsOwnLock(current)
            ? await cloudProvider.HeartbeatSessionLockAsync(world.Game, world.WorldId, current!.LockId, LockTtl, cancellationToken)
            : null;
        if (renewed is null)
        {
            lastHeartbeats.TryRemove(key, out _);
            logger.Warning(AppLogKeyword.CloudSession, "Session lock for world {WorldId} was lost", world.WorldId);
            return new CloudSyncResult(false, CloudSyncState.Conflict, current is null || IsOwnLock(current)
                ? "Your session lock was removed. Friends may start this world while you play."
                : "Your session lock expired and another player took over.");
        }

        logger.Debug(AppLogKeyword.CloudSession, "Session lock for world {WorldId} renewed until {ExpiresAt}", world.WorldId, renewed.ExpiresAtUtc);
        return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, "Session lock renewed.");
    }

    public async Task<CloudSyncResult> EndSessionAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        logger.Debug(AppLogKeyword.CloudSession, "Ending cloud session for world {WorldId}", world.WorldId);

        var status = await RefreshStatusAsync(world, cancellationToken);
        if (!status.Connection.IsConnected)
        {
            logger.Warning(AppLogKeyword.CloudSession, "End session blocked because provider is not connected for world {WorldId}", world.WorldId);
            return new CloudSyncResult(false, status.State, "Cloud sync is not connected.");
        }

        if (status.SessionLock is null)
        {
            return new CloudSyncResult(true, status.State, "No active session lock exists.");
        }

        if (!IsOwnLock(status.SessionLock))
        {
            logger.Warning(AppLogKeyword.CloudSession, "End session blocked because {PlayerName} owns lock for world {WorldId}", status.SessionLock.PlayerName, world.WorldId);
            return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{status.SessionLock.PlayerName} owns the active session lock.");
        }

        await cloudProvider.ClearSessionLockAsync(world.Game, world.WorldId, status.SessionLock.LockId, cancellationToken);
        lastHeartbeats.TryRemove(HeartbeatKey(world), out _);
        logger.Information(AppLogKeyword.CloudSession, "Cloud session ended for world {WorldId}", world.WorldId);
        return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Session ended for {world.WorldName}.");
    }
}
