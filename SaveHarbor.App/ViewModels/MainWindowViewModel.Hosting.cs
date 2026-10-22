using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// Who may host the selected world right now, for the Sharing panel's hosting row. Direction picks its icon
// (SkinSyncIcon): "lock" while someone holds the world, "unlock" when it is free.
public sealed record HostingInfo(string Title, string Detail, SyncTone Tone, string Direction);

public partial class MainWindowViewModel
{
    private int relativeTimeTicks;

    public HostingInfo? Hosting
    {
        get
        {
            if (SelectedWorldStatus is not { IsConnected: true } status || status.State == CloudSyncState.UpdateRequired)
            {
                return null;
            }

            if (status.SessionLock is not { } sessionLock)
            {
                return new HostingInfo(
                    "Free to host",
                    "Nobody is playing this world. Play marks it as yours, so no one else hosts it at the same time.",
                    SyncTone.Good,
                    "unlock");
            }

            var started = sessionLock.StartedAtUtc.ToLocalTime();
            var expires = sessionLock.ExpiresAtUtc.ToLocalTime();
            if (status.IsOwnSession)
            {
                return new HostingInfo(
                    "You are hosting",
                    $"Since {started:HH:mm}, from v{sessionLock.BasedOnVersionNumber}. The lock is renewed while the game runs; closing the game shares your progress and frees the world.",
                    SyncTone.Info,
                    "lock");
            }

            return sessionLock.IsLiveAt(DateTimeOffset.UtcNow)
                ? new HostingInfo(
                    $"{sessionLock.PlayerName} is hosting",
                    $"Since {started:HH:mm}, from v{sessionLock.BasedOnVersionNumber}. The world is locked until {expires:HH:mm} unless their game keeps it.",
                    SyncTone.Warning,
                    "lock")
                : new HostingInfo(
                    $"{sessionLock.PlayerName}'s session expired",
                    $"It ran out at {expires:HH:mm} without being renewed. You can take over with Play; check their progress was shared first.",
                    SyncTone.Neutral,
                    "unlock");
        }
    }

    // When the selected world was last compared with the cloud.
    public string CloudCheckedText => SelectedWorldStatus?.LocalState.LastCloudCheckAtUtc is { } checkedAt
        ? $"Checked {DisplayFormatter.FormatAge(checkedAt.ToLocalTime())}"
        : string.Empty;

    private void NotifyHosting()
    {
        OnPropertyChanged(nameof(Hosting));
        OnPropertyChanged(nameof(CloudCheckedText));
    }

    // "2 min ago" texts and lock expiry move on with the clock; refreshed every 30 s from the game monitor.
    private void NotifyRelativeTimes()
    {
        if (++relativeTimeTicks % 6 != 0)
        {
            return;
        }

        NotifyHosting();
        OnPropertyChanged(nameof(LocalSide));
        OnPropertyChanged(nameof(CloudSide));
        OnPropertyChanged(nameof(SelectedWorldSavedText));
    }
}
