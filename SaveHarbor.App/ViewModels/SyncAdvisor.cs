using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public enum SyncTone
{
    Neutral,
    Good,
    Info,
    Warning,
    Danger
}

public enum SyncAction
{
    None,
    Connect,
    Upload,
    Download
}

public sealed record SyncBadge(string Text, SyncTone Tone);

// What the player should do about the selected world, in plain words. Direction shows where the newer copy is:
// "→" this PC has the newer copy, "←" the cloud has it, "=" both are the same, "≠" both changed.
public sealed record SyncAdvice(string Headline, string Explanation, SyncAction Action, string ActionLabel, SyncTone Tone, string Direction)
{
    public bool HasAction => Action != SyncAction.None;
}

// One side of the "this PC versus cloud" comparison.
public sealed record SyncSide(string Version, string Detail, string When);

public static class SyncAdvisor
{
    public static SyncBadge? Badge(CloudSyncState state, bool hasLocalChanges) => state switch
    {
        CloudSyncState.ConnectedNoCloudSave => new SyncBadge("Only on this PC", SyncTone.Neutral),
        CloudSyncState.UpToDate => new SyncBadge("Up to date", SyncTone.Good),
        CloudSyncState.LocalNewerUploadSafe => new SyncBadge("Upload needed", SyncTone.Info),
        CloudSyncState.CloudNewer => new SyncBadge("Download needed", SyncTone.Warning),
        CloudSyncState.Conflict => new SyncBadge(hasLocalChanges ? "Both changed" : "Out of step", SyncTone.Danger),
        CloudSyncState.SomeonePlaying => new SyncBadge("Being played", SyncTone.Warning),
        _ => null
    };

    public static SyncAdvice Advise(CloudSyncStatus status)
    {
        var latest = status.LatestVersion;
        var localBase = status.LocalState.LocalBaseVersionNumber;
        return status.State switch
        {
            CloudSyncState.NotConnected => new SyncAdvice(
                "Cloud not connected",
                "Connect Google Drive to compare this world with your group's shared copy.",
                SyncAction.Connect, "Connect", SyncTone.Neutral, "?"),
            CloudSyncState.ConnectedNoCloudSave => new SyncAdvice(
                "Only on this PC",
                "The shared folder has no copy of this world yet. Upload it to share it with your group.",
                SyncAction.Upload, "Upload to cloud", SyncTone.Info, "→"),
            CloudSyncState.UpToDate => new SyncAdvice(
                "Up to date",
                $"This PC and the cloud both have v{latest!.VersionNumber}. Nothing to do; you can play.",
                SyncAction.None, string.Empty, SyncTone.Good, "="),
            CloudSyncState.LocalNewerUploadSafe => new SyncAdvice(
                "Your copy is newer",
                $"You played after v{latest!.VersionNumber} and have not uploaded yet. Upload so your group continues from your progress.",
                SyncAction.Upload, "Upload my progress", SyncTone.Info, "→"),
            CloudSyncState.CloudNewer when localBase is null => new SyncAdvice(
                "Cloud copy found",
                $"The cloud has v{latest!.VersionNumber} by {latest.UploadedBy}, but the copy on this PC has never been synced. Download the shared copy before you play; your copy is backed up first.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Warning, "←"),
            CloudSyncState.CloudNewer => new SyncAdvice(
                "Cloud is newer",
                $"{latest!.UploadedBy} uploaded v{latest.VersionNumber} after your v{localBase}. Download before you play, so you do not continue from an old save.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Warning, "←"),
            CloudSyncState.Conflict when status.HasLocalChanges => new SyncAdvice(
                "Both changed",
                $"{latest!.UploadedBy} uploaded v{latest.VersionNumber}, and you also played since v{localBase}. Only one can continue: downloading replaces your progress (it is backed up first). Agree with your group before choosing.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Danger, "≠"),
            CloudSyncState.Conflict => new SyncAdvice(
                "Out of step",
                $"This PC says v{localBase}, but the cloud only has v{latest!.VersionNumber}; a newer version may have been removed. Download v{latest.VersionNumber} or check with your group.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Danger, "≠"),
            CloudSyncState.SomeonePlaying => new SyncAdvice(
                $"{status.SessionLock!.PlayerName} is playing",
                $"They started from v{status.SessionLock.BasedOnVersionNumber}. Wait until they finish and upload, then download their progress.",
                SyncAction.None, string.Empty, SyncTone.Warning, "…"),
            _ => new SyncAdvice(status.Title, status.Detail, SyncAction.None, string.Empty, SyncTone.Danger, "!")
        };
    }

    public static SyncSide DescribeLocal(GameWorld world, CloudSyncStatus status)
    {
        var localBase = status.LocalState.LocalBaseVersionNumber;
        var when = $"Saved {DisplayFormatter.FormatAge(world.LastModifiedAt)}";
        if (localBase is null)
        {
            return new SyncSide("Not synced yet", "Never uploaded or downloaded", when);
        }

        return status.HasLocalChanges
            ? new SyncSide($"v{localBase} + new progress", "Played since then, not uploaded", when)
            : new SyncSide($"v{localBase}", "No changes since then", when);
    }

    public static SyncSide DescribeCloud(CloudSyncStatus status)
    {
        if (!status.IsConnected)
        {
            return new SyncSide("Unknown", "Not connected", string.Empty);
        }

        var latest = status.LatestVersion;
        return latest is null
            ? new SyncSide("Nothing yet", "No one has uploaded this world", string.Empty)
            : new SyncSide($"v{latest.VersionNumber}", $"Uploaded by {latest.UploadedBy}", DisplayFormatter.FormatAge(latest.UploadedAtUtc.ToLocalTime()));
    }
}
