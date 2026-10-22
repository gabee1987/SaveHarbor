using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

// What Play will do for the selected world, said in its tooltip before the click. Read from the last cloud refresh,
// cached or live; Play itself checks the cloud again.
public partial class MainWindowViewModel
{
    // Each local world's state from the last overview, by world id.
    private IReadOnlyDictionary<string, WorldSyncSummary> worldSummaries = new Dictionary<string, WorldSyncSummary>();

    public string PlayPreflightText
    {
        get
        {
            if (SelectedWorld is null)
            {
                return string.Empty;
            }

            // The selected world's own status is newer than the overview after a session started or ended.
            var text = SelectedWorldStatus is { IsConnected: true } status
                ? DescribePlay(status.State, status.LocalState.LocalBaseVersionNumber, status.LatestVersion, status.SessionLock?.PlayerName)
                : worldSummaries.GetValueOrDefault(SelectedWorld.WorldId) is { } summary
                    ? DescribePlay(summary.State, summary.LocalBaseVersion, summary.LatestVersion, null)
                    : string.Empty;

            return text.Length > 0 && SelectedWorldStatus is null && CloudStaleSince is { } at
                ? $"{text} (as of {at.ToLocalTime():HH:mm})"
                : text;
        }
    }

    public bool HasPlayPreflight => PlayPreflightText.Length > 0;

    private string DescribePlay(CloudSyncState state, int? localBase, CloudVersionMetadata? latest, string? playerName) => state switch
    {
        CloudSyncState.SomeonePlaying => $"{playerName ?? "Someone in your group"} is playing this world now, so it cannot be hosted until they finish.",
        CloudSyncState.ConnectedNoCloudSave => "This world is not shared yet: upload it first, then Play locks it and starts the game.",
        CloudSyncState.CloudNewer when latest is not null => localBase is null
            ? $"Cloud has v{latest.VersionNumber} by {latest.UploadedBy}: download it before playing."
            : $"Cloud has v{latest.VersionNumber} by {latest.UploadedBy}, you have v{localBase}: download it before playing.",
        CloudSyncState.Conflict when latest is not null =>
            $"Cloud has v{latest.VersionNumber} by {latest.UploadedBy}, and you have played since v{localBase}: sort this out before playing.",
        CloudSyncState.LocalNewerUploadSafe when latest is not null =>
            $"You have v{latest.VersionNumber} plus progress not shared yet: Play locks the world and starts {ActiveGameName}.",
        CloudSyncState.UpToDate when latest is not null =>
            $"You have the newest version, v{latest.VersionNumber} by {latest.UploadedBy}: Play locks the world and starts {ActiveGameName}.",
        _ => string.Empty
    };

    private void NotifyPlayPreflight()
    {
        OnPropertyChanged(nameof(PlayPreflightText));
        OnPropertyChanged(nameof(HasPlayPreflight));
    }
}
