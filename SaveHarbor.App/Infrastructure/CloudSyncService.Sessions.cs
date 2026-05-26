using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
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
}
