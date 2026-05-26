using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudSyncService
{
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
}
