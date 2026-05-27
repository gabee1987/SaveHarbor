using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    public async Task<CloudSyncResult> StartSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        logger.Information(AppLogKeyword.CloudSession, "Starting cloud session for world {WorldId}", world.WorldId);

        var status = await RefreshStatusAsync(world, cancellationToken);
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

        if (IsActiveOtherPlayerLock(status.SessionLock))
        {
            logger.Warning(AppLogKeyword.CloudSession, "Start session blocked by active lock from {PlayerName} for world {WorldId}", status.SessionLock!.PlayerName, world.WorldId);
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
        logger.Information(AppLogKeyword.CloudSession, "Cloud session started for world {WorldId} from version {VersionNumber}", world.WorldId, status.LatestVersion.VersionNumber);
        return new CloudSyncResult(true, CloudSyncState.SomeonePlaying, $"Session started for {world.WorldName} from v{status.LatestVersion.VersionNumber}.");
    }

    public async Task<CloudSyncResult> EndSessionAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        logger.Information(AppLogKeyword.CloudSession, "Ending cloud session for world {WorldId}", world.WorldId);

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

        await cloudProvider.ClearSessionLockAsync(world.WorldId, status.SessionLock.LockId, cancellationToken);
        logger.Information(AppLogKeyword.CloudSession, "Cloud session ended for world {WorldId}", world.WorldId);
        return new CloudSyncResult(true, CloudSyncState.UpToDate, $"Session ended for {world.WorldName}.");
    }
}
