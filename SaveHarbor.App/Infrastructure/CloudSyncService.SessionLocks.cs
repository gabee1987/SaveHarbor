using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private TimeSpan LockTtl => TimeSpan.FromSeconds(options.LockTtlSeconds);

    private TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds);

    // now is the provider's clock when it has one, so a PC with a wrong clock does not misjudge expiry.
    private bool IsActiveOtherPlayerLock(CloudSessionLock? sessionLock, DateTimeOffset now)
    {
        return sessionLock is not null && sessionLock.IsLiveAt(now) && !IsOwnLock(sessionLock);
    }

    // Locks written before version 2.3 have no DeviceId and are matched by computer name.
    private bool IsOwnLock(CloudSessionLock? sessionLock)
    {
        return sessionLock is not null && sessionLock.IsHeldBy(deviceIdentity.DeviceId, Environment.MachineName);
    }

    private bool IsActiveOwnLock(CloudSessionLock? sessionLock, DateTimeOffset now)
    {
        return IsOwnLock(sessionLock) && sessionLock!.IsLiveAt(now);
    }

    private static string HeartbeatKey(GameWorld world) => $"{world.Game.ToStorageKey()}:{world.WorldId}";
}
