using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

// How a local world relates to its shared cloud copy. Used for the selected world's full status and for the badge of
// every world in the list, so the two always agree.
public static class SyncStateClassifier
{
    public static CloudSyncState Classify(int? localBaseVersion, int? latestCloudVersion, bool hasLocalChanges, bool otherPlayerPlaying)
    {
        if (latestCloudVersion is null)
        {
            return CloudSyncState.ConnectedNoCloudSave;
        }

        if (otherPlayerPlaying)
        {
            return CloudSyncState.SomeonePlaying;
        }

        if (localBaseVersion is null)
        {
            return CloudSyncState.CloudNewer;
        }

        if (localBaseVersion < latestCloudVersion)
        {
            return hasLocalChanges ? CloudSyncState.Conflict : CloudSyncState.CloudNewer;
        }

        if (localBaseVersion > latestCloudVersion)
        {
            return CloudSyncState.Conflict;
        }

        return hasLocalChanges ? CloudSyncState.LocalNewerUploadSafe : CloudSyncState.UpToDate;
    }

    // Local changes only matter once the world is linked to a cloud version that nobody else is playing; skipping
    // the check otherwise avoids hashing saves for nothing.
    public static bool NeedsLocalChangeCheck(int? localBaseVersion, int? latestCloudVersion, bool otherPlayerPlaying) =>
        latestCloudVersion is not null && localBaseVersion is not null && !otherPlayerPlaying;
}
