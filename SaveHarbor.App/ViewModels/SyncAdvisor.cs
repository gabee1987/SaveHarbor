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

// Which copy has progress the other lacks; the comparison highlights that side.
public enum NewerSide
{
    None,
    Local,
    Cloud,
    Both
}

public enum SyncAction
{
    None,
    Connect,
    Upload,
    Download
}

public sealed record SyncBadge(string Text, SyncTone Tone);

// What the player should do about the selected world, in plain words. Direction says where the newer copy is, and
// picks the comparison's icon (SkinSyncIcon): "→" this PC has the newer copy, "←" the cloud has it, "=" both are the
// same, "≠" both changed, "…" a friend plays, "!" blocked, "?" not checked. Relation says it in a word or two, under
// the icon.
public sealed record SyncAdvice(string Headline, string Explanation, SyncAction Action, string ActionLabel, SyncTone Tone, string Direction)
{
    public bool HasAction => Action != SyncAction.None;

    public string Relation { get; init; } = string.Empty;

    // What happens after the advised step, so the player knows what to expect; empty when there is nothing to add.
    public string Hint { get; init; } = string.Empty;

    public bool HasHint => Hint.Length > 0;

    public NewerSide Newer { get; init; }

    public bool LocalIsNewer => Newer == NewerSide.Local;

    public bool CloudIsNewer => Newer == NewerSide.Cloud;

    public bool BothChanged => Newer == NewerSide.Both;
}

// One side of the "this PC versus cloud" comparison. HasNews marks progress the other side does not have yet; Extra
// adds the size and, for this PC, when it last matched the cloud.
public sealed record SyncSide(string Version, string Detail, string When, bool HasNews = false)
{
    public string Extra { get; init; } = string.Empty;

    public bool HasExtra => Extra.Length > 0;
}

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
        CloudSyncState.UpdateRequired => new SyncBadge("Update required", SyncTone.Warning),
        _ => null
    };

    // A provider that knows what is missing (SaveHarbor Cloud) says so in its own words.
    private static SyncAdvice AdviseSetup(CloudConnectionStatus connection) => connection.Reason switch
    {
        CloudConnectionReason.SignIn => new SyncAdvice("Sign in to share", connection.Message, SyncAction.Connect, "Sign in", SyncTone.Neutral, "?") { Relation = "Not checked" },
        CloudConnectionReason.Group => new SyncAdvice("Choose a group", connection.Message, SyncAction.Connect, "Choose group", SyncTone.Neutral, "?") { Relation = "Not checked" },
        CloudConnectionReason.Unreachable => new SyncAdvice("Cloud unreachable", connection.Message, SyncAction.Connect, "Try again", SyncTone.Warning, "?") { Relation = "Not checked" },
        CloudConnectionReason.UpdateRequired => new SyncAdvice("Update required", connection.Message, SyncAction.None, string.Empty, SyncTone.Warning, "!") { Relation = "Blocked" },
        _ => new SyncAdvice("Cloud not connected", connection.Message, SyncAction.None, string.Empty, SyncTone.Neutral, "?") { Relation = "Not checked" }
    };

    public static SyncAdvice Advise(CloudSyncStatus status)
    {
        var latest = status.LatestVersion;
        var localBase = status.LocalState.LocalBaseVersionNumber;
        return status.State switch
        {
            CloudSyncState.UpdateRequired => new SyncAdvice(
                "Update required", status.Detail, SyncAction.None, string.Empty, SyncTone.Warning, "!") { Relation = "Blocked" },
            CloudSyncState.NotConnected when status.Connection.Reason.Length > 0 => AdviseSetup(status.Connection),
            CloudSyncState.NotConnected => new SyncAdvice(
                "Cloud not connected",
                "Connect Google Drive to compare this world with your group's shared copy.",
                SyncAction.Connect, "Connect", SyncTone.Neutral, "?") { Relation = "Not checked" },
            CloudSyncState.ConnectedNoCloudSave => new SyncAdvice(
                "Only on this PC",
                "The shared folder has no copy of this world yet. Upload it to share it with your group.",
                SyncAction.Upload, "Upload to cloud", SyncTone.Info, "→")
            {
                Relation = "Only here",
                Newer = NewerSide.Local,
                Hint = "After the upload your friends see it under Shared worlds and can download it."
            },
            CloudSyncState.UpToDate => new SyncAdvice(
                "Up to date",
                $"This PC and the cloud both have v{latest!.VersionNumber}. Nothing to do; you can play.",
                SyncAction.None, string.Empty, SyncTone.Good, "=")
            {
                Relation = "Identical",
                Hint = "Play marks the world as yours while you host, and shares your progress when you close the game."
            },
            CloudSyncState.LocalNewerUploadSafe => new SyncAdvice(
                "Your copy is newer",
                $"You played after v{latest!.VersionNumber} and have not uploaded yet. Upload so your group continues from your progress.",
                SyncAction.Upload, "Upload my progress", SyncTone.Info, "→")
            {
                Relation = "PC is ahead",
                Newer = NewerSide.Local,
                Hint = "The upload becomes the next version; friends download it before they play."
            },
            CloudSyncState.CloudNewer when localBase is null => new SyncAdvice(
                "Cloud copy found",
                $"The cloud has v{latest!.VersionNumber} by {latest.UploadedBy}, but the copy on this PC has never been synced. Download the shared copy before you play; your copy is backed up first.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Warning, "←")
            {
                Relation = "Cloud is ahead",
                Newer = NewerSide.Cloud,
                Hint = "This PC's copy is kept as a backup before it is replaced."
            },
            CloudSyncState.CloudNewer => new SyncAdvice(
                "Cloud is newer",
                $"{latest!.UploadedBy} uploaded v{latest.VersionNumber} after your v{localBase}. Download before you play, so you do not continue from an old save.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Warning, "←")
            {
                Relation = "Cloud is ahead",
                Newer = NewerSide.Cloud,
                Hint = "This PC's copy is kept as a backup before it is replaced, so nothing is lost."
            },
            CloudSyncState.Conflict when status.HasLocalChanges => new SyncAdvice(
                "Both changed",
                $"{latest!.UploadedBy} uploaded v{latest.VersionNumber}, and you also played since v{localBase}. Only one can continue: downloading replaces your progress (it is backed up first). Agree with your group before choosing.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Danger, "≠")
            {
                Relation = "Both changed",
                Newer = NewerSide.Both,
                Hint = "Your own progress stays in Backups after a download, should the group pick it after all."
            },
            CloudSyncState.Conflict => new SyncAdvice(
                "Out of step",
                $"This PC says v{localBase}, but the cloud only has v{latest!.VersionNumber}; a newer version may have been removed. Download v{latest.VersionNumber} or check with your group.",
                SyncAction.Download, $"Download v{latest.VersionNumber}", SyncTone.Danger, "≠") { Relation = "Both changed", Newer = NewerSide.Both },
            CloudSyncState.SomeonePlaying => new SyncAdvice(
                $"{status.SessionLock!.PlayerName} is playing",
                $"They started from v{status.SessionLock.BasedOnVersionNumber}. Wait until they finish and upload, then download their progress.",
                SyncAction.None, string.Empty, SyncTone.Warning, "…")
            {
                Relation = "In use",
                Hint = "When they close the game, their progress is shared and this panel says what to download."
            },
            _ => new SyncAdvice(status.Title, status.Detail, SyncAction.None, string.Empty, SyncTone.Danger, "!") { Relation = "Problem" }
        };
    }

    public static SyncSide DescribeLocal(GameWorld world, CloudSyncStatus status)
    {
        var localBase = status.LocalState.LocalBaseVersionNumber;
        var when = $"Saved {DisplayFormatter.FormatAge(world.LastModifiedAt)}";
        if (localBase is null)
        {
            return new SyncSide("Unsynced", "Never uploaded or downloaded", when, HasNews: status.LatestVersion is null);
        }

        var extra = DescribeLastSync(world, status.LocalState);
        return status.HasLocalChanges
            ? new SyncSide($"v{localBase}", "+ new progress, not uploaded", when, HasNews: true) { Extra = extra }
            : new SyncSide($"v{localBase}", "No changes since", when) { Extra = extra };
    }

    // Size, and when this PC last matched the cloud: the player's own upload or a download.
    private static string DescribeLastSync(GameWorld world, LocalSyncState state)
    {
        var size = DisplayFormatter.FormatBytes(world.SizeBytes);
        var uploaded = state.LastUploadedAtUtc;
        var downloaded = state.LastDownloadedAtUtc;
        if (uploaded is null && downloaded is null)
        {
            return size;
        }

        return uploaded >= downloaded || downloaded is null
            ? $"{size} · you uploaded {DisplayFormatter.FormatAge(uploaded!.Value.ToLocalTime())}"
            : $"{size} · downloaded {DisplayFormatter.FormatAge(downloaded.Value.ToLocalTime())}";
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
            : new SyncSide(
                $"v{latest.VersionNumber}",
                $"by {latest.UploadedBy}",
                $"Uploaded {DisplayFormatter.FormatAge(latest.UploadedAtUtc.ToLocalTime())}",
                HasNews: latest.VersionNumber > (status.LocalState.LocalBaseVersionNumber ?? 0))
            {
                Extra = latest.ArchiveSizeBytes > 0 ? $"{DisplayFormatter.FormatBytes(latest.ArchiveSizeBytes)} archive" : string.Empty
            };
    }
}
