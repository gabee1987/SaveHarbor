using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public sealed record FriendActivity(string Title, string Message);

// What friends did between two live overviews of the same game and group: new versions, sessions started and
// finished. Read from the cloud state itself rather than from the realtime message, so polling finds the same events.
public static class CloudFriendActivity
{
    // ownDeviceId leaves out this PC's own sessions. A version this PC already has (its local base, right after its own
    // upload or download) is not news either.
    public static IReadOnlyList<FriendActivity> Compare(
        CloudWorldOverview before,
        CloudWorldOverview after,
        string ownDeviceId,
        Func<string, string> worldName)
    {
        var activity = new List<FriendActivity>();
        var previous = LatestVersions(before);
        foreach (var (worldId, (latest, localBase)) in LatestVersions(after))
        {
            var known = previous.TryGetValue(worldId, out var old) ? old.Latest.VersionNumber : 0;
            if (latest.VersionNumber > known && localBase != latest.VersionNumber)
            {
                activity.Add(new FriendActivity("New version shared", $"{latest.UploadedBy} shared v{latest.VersionNumber} of {worldName(worldId)}."));
            }
        }

        foreach (var (worldId, sessionLock) in before.LocksByWorldId)
        {
            if (!IsOwn(sessionLock, ownDeviceId) && !IsSameLock(after, worldId, sessionLock))
            {
                activity.Add(new FriendActivity("Session finished", $"{sessionLock.PlayerName} finished playing {worldName(worldId)}."));
            }
        }

        foreach (var (worldId, sessionLock) in after.LocksByWorldId)
        {
            if (!IsOwn(sessionLock, ownDeviceId) && !IsSameLock(before, worldId, sessionLock))
            {
                activity.Add(new FriendActivity("Friend playing", $"{sessionLock.PlayerName} started playing {worldName(worldId)}."));
            }
        }

        return activity;
    }

    private static Dictionary<string, (CloudVersionMetadata Latest, int? LocalBase)> LatestVersions(CloudWorldOverview overview)
    {
        var versions = new Dictionary<string, (CloudVersionMetadata, int?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in overview.LocalWorlds)
        {
            if (summary.LatestVersion is { } latest)
            {
                versions[summary.WorldId] = (latest, summary.LocalBaseVersion);
            }
        }

        foreach (var manifest in overview.CloudOnlyWorlds)
        {
            if (manifest.LatestVersion is { } latest)
            {
                versions.TryAdd(manifest.WorldId, (latest, null));
            }
        }

        return versions;
    }

    private static bool IsOwn(CloudSessionLock sessionLock, string ownDeviceId) =>
        string.Equals(sessionLock.DeviceId, ownDeviceId, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameLock(CloudWorldOverview overview, string worldId, CloudSessionLock sessionLock) =>
        overview.LocksByWorldId.TryGetValue(worldId, out var other) && other.LockId == sessionLock.LockId;
}
