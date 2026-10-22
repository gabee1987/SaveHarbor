using System.Windows.Threading;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

// SaveHarbor Cloud pushes the group's changes, so the window re-reads the cloud when a friend uploads or starts or ends
// a session instead of polling. A change only starts a refresh; what changed is read from the refreshed state. While
// the live connection is down, the cloud is checked every minute.
public partial class MainWindowViewModel
{
    private static readonly TimeSpan LiveChangeDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LivePollingInterval = TimeSpan.FromSeconds(60);

    // A friend's upload changes several rows at once; one refresh after the last of them.
    private DispatcherTimer? liveChangeTimer;
    private DispatcherTimer? livePollingTimer;

    // The last live overview and the group or folder it belongs to, to tell what friends did since.
    private CloudWorldOverview? lastLiveOverview;
    private string lastLiveOverviewScope = string.Empty;

    public string LiveStateText => !ShowsSharingConnection ? string.Empty : _live.State switch
    {
        CloudLiveState.Live => "Live",
        CloudLiveState.Connecting => "Connecting",
        CloudLiveState.Polling => "Reconnecting",
        _ => string.Empty
    };

    public bool HasLiveState => LiveStateText.Length > 0;

    public SyncTone LiveStateTone => _live.State switch
    {
        CloudLiveState.Live => SyncTone.Good,
        CloudLiveState.Polling => SyncTone.Warning,
        _ => SyncTone.Neutral
    };

    public string LiveStateTooltip => _live.State == CloudLiveState.Polling
        ? "The live connection dropped. SaveHarbor checks the cloud every minute and reconnects on its own."
        : "Friends' uploads and sessions show up here within seconds.";

    private int OnlineMemberCount => _live.State == CloudLiveState.Live ? _live.Online.Count : 0;

    private void SubscribeLiveUpdates()
    {
        liveChangeTimer = new DispatcherTimer { Interval = LiveChangeDebounce };
        liveChangeTimer.Tick += OnLiveChangeTimerTick;
        livePollingTimer = new DispatcherTimer { Interval = LivePollingInterval };
        livePollingTimer.Tick += OnLivePollingTick;

        _live.ChangeReceived += (_, _) => OnUiThread(() =>
        {
            liveChangeTimer.Stop();
            liveChangeTimer.Start();
        });
        _live.StateChanged += (_, _) => OnUiThread(OnLiveStateChanged);
        _live.PresenceChanged += (_, _) => OnUiThread(() => OnPropertyChanged(nameof(SharingHeroText)));
    }

    private static void OnUiThread(Action action) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(action);

    // Connected while SaveHarbor Cloud shares the active game through a group; disconnected otherwise. Called after
    // every cloud refresh, which follows every provider, game, group and sign-in change.
    private Task SyncLiveConnectionAsync()
    {
        var game = _activeGame.Current.Id;
        return ShowsSharingConnection
            && _cloudGroups.IsSignedIn
            && SharingConnection is { IsConnected: true }
            && _cloudGroups.GetActiveGroupId(game) is { Length: > 0 } groupId
                ? _live.StartAsync(groupId, _settingsStore.Current.ShowOnlineStatus
                    ? new CloudPresence(_playerIdentity.DisplayName, game.ToStorageKey(), HasOwnCloudSession() ? SelectedWorld?.WorldId : null)
                    : null)
                : _live.StopAsync();
    }

    private void OnLiveStateChanged()
    {
        OnPropertyChanged(nameof(LiveStateText));
        OnPropertyChanged(nameof(HasLiveState));
        OnPropertyChanged(nameof(LiveStateTone));
        OnPropertyChanged(nameof(LiveStateTooltip));
        OnPropertyChanged(nameof(SharingHeroText));

        if (_live.State == CloudLiveState.Polling)
        {
            livePollingTimer?.Start();
            return;
        }

        livePollingTimer?.Stop();
        if (_live.State == CloudLiveState.Live)
        {
            // Whatever changed while connecting or disconnected.
            _ = RefreshCloudAsync("live");
        }
    }

    // A command already refreshes when it ends; the change is picked up after it.
    private async void OnLiveChangeTimerTick(object? sender, EventArgs e)
    {
        if (IsBusy || isAutoEndingSession)
        {
            return;
        }

        liveChangeTimer?.Stop();
        await RefreshCloudAsync("realtime");
    }

    private async void OnLivePollingTick(object? sender, EventArgs e)
    {
        if (!IsBusy && !isAutoEndingSession)
        {
            await RefreshCloudAsync("polling");
        }
    }

    // Toasts for what friends did since the last live refresh of the same group; a first refresh or a switch only
    // remembers the state.
    private void ShowFriendActivity(GameId game, CloudWorldOverview overview)
    {
        var scope = CloudProviderKind.ToStorageKey(_providerSelector.ActiveKind) + "/" + game.ToStorageKey() + "/" + CloudCacheScope(game);
        var before = lastLiveOverviewScope == scope ? lastLiveOverview : null;
        lastLiveOverview = overview.IsConnected ? overview : null;
        lastLiveOverviewScope = scope;
        if (before is null || !overview.IsConnected)
        {
            return;
        }

        var activity = CloudFriendActivity.Compare(before, overview, _deviceIdentity.DeviceId, worldId => WorldName(overview, worldId));
        foreach (var item in activity)
        {
            AddActivity("Info", item.Message);
        }

        // Several at once (a reconnect after a long gap) become one toast.
        if (activity.Count == 1)
        {
            _toastService.Info(activity[0].Title, activity[0].Message);
        }
        else if (activity.Count > 1)
        {
            _toastService.Info("Your group was busy", $"{activity[0].Message} And {activity.Count - 1} more in the activity log.");
        }
    }

    private string WorldName(CloudWorldOverview overview, string worldId) =>
        Worlds.FirstOrDefault(world => string.Equals(world.WorldId, worldId, StringComparison.OrdinalIgnoreCase))?.WorldName
        ?? overview.CloudOnlyWorlds.FirstOrDefault(manifest => string.Equals(manifest.WorldId, worldId, StringComparison.OrdinalIgnoreCase))?.WorldName
        ?? worldId;
}
