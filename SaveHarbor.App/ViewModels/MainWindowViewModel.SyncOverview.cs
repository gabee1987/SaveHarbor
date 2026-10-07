using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// A shared world that is not on this PC. Name and uploader come from the cloud and are only displayed.
public sealed record CloudWorldEntry(string WorldId, string WorldName, string VersionText, string Detail)
{
    public static CloudWorldEntry From(CloudWorldManifest manifest)
    {
        var latest = manifest.LatestVersion!;
        var name = string.IsNullOrWhiteSpace(manifest.WorldName) ? manifest.WorldId : manifest.WorldName;
        return new CloudWorldEntry(
            manifest.WorldId,
            name,
            $"v{latest.VersionNumber}",
            $"v{latest.VersionNumber} by {latest.UploadedBy} · {DisplayFormatter.FormatAge(latest.UploadedAtUtc.ToLocalTime())}");
    }
}

public partial class MainWindowViewModel
{
    // Badge per local world id. Replaced as a whole, so the list re-reads it.
    [ObservableProperty]
    private IReadOnlyDictionary<string, SyncBadge> worldSyncBadges = new Dictionary<string, SyncBadge>();

    public ObservableCollection<CloudWorldEntry> CloudOnlyWorlds { get; } = [];

    // Null while the status still belongs to the previously selected world.
    private CloudSyncStatus? SelectedWorldStatus =>
        CloudStatus is not null && SelectedWorld is not null
        && string.Equals(CloudStatus.LocalState.WorldId, SelectedWorld.WorldId, StringComparison.OrdinalIgnoreCase)
            ? CloudStatus
            : null;

    public SyncAdvice? SelectedSyncAdvice => SelectedWorldStatus is { } status ? SyncAdvisor.Advise(status) : null;

    public SyncSide? LocalSide => SelectedWorldStatus is { } status ? SyncAdvisor.DescribeLocal(SelectedWorld!, status) : null;

    public SyncSide? CloudSide => SelectedWorldStatus is { } status ? SyncAdvisor.DescribeCloud(status) : null;

    public SyncBadge? SelectedWorldBadge => SelectedWorldStatus is { IsConnected: true } status
        ? SyncAdvisor.Badge(status.State, status.HasLocalChanges)
        : null;

    private void NotifySyncComparison()
    {
        OnPropertyChanged(nameof(SelectedSyncAdvice));
        OnPropertyChanged(nameof(LocalSide));
        OnPropertyChanged(nameof(CloudSide));
        OnPropertyChanged(nameof(SelectedWorldBadge));
        RunRecommendedSyncActionCommand.NotifyCanExecuteChanged();
    }

    // The selected world's full status also reads its session lock, so its badge is taken from there.
    private void ApplySelectedStatusToBadges(CloudSyncStatus? status)
    {
        if (SelectedWorldStatus is null || status is not { IsConnected: true })
        {
            return;
        }

        var badges = new Dictionary<string, SyncBadge>(WorldSyncBadges, StringComparer.OrdinalIgnoreCase);
        var badge = SyncAdvisor.Badge(status.State, status.HasLocalChanges);
        if (badge is null)
        {
            badges.Remove(status.LocalState.WorldId);
        }
        else
        {
            badges[status.LocalState.WorldId] = badge;
        }

        WorldSyncBadges = badges;
    }

    private async Task RefreshCloudOverviewAsync()
    {
        var game = _activeGame.Current.Id;
        try
        {
            var overview = await _cloudSyncService.GetOverviewAsync(game, Worlds.ToArray());
            if (game != _activeGame.Current.Id)
            {
                return;
            }

            WorldSyncBadges = overview.LocalWorlds
                .Select(summary => (summary.WorldId, Badge: SyncAdvisor.Badge(summary.State, summary.HasLocalChanges)))
                .Where(item => item.Badge is not null)
                .ToDictionary(item => item.WorldId, item => item.Badge!, StringComparer.OrdinalIgnoreCase);

            CloudOnlyWorlds.Clear();
            foreach (var manifest in overview.CloudOnlyWorlds)
            {
                CloudOnlyWorlds.Add(CloudWorldEntry.From(manifest));
            }

            ApplySelectedStatusToBadges(CloudStatus);
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Refresh cloud world list", AppLogKeyword.CloudSync);
            AddActivity("Error", FormatActivityError(error));
        }
    }

    private void ClearCloudOverview()
    {
        WorldSyncBadges = new Dictionary<string, SyncBadge>();
        CloudOnlyWorlds.Clear();
    }

    private bool CanRunRecommendedSyncAction() => !IsBusy && SelectedSyncAdvice?.HasAction == true;

    [RelayCommand(CanExecute = nameof(CanRunRecommendedSyncAction))]
    private async Task RunRecommendedSyncActionAsync()
    {
        switch (SelectedSyncAdvice?.Action)
        {
            case SyncAction.Connect:
                await ConnectCloudCommand.ExecuteAsync(null);
                break;
            case SyncAction.Upload:
                await UploadCloudCommand.ExecuteAsync(null);
                break;
            case SyncAction.Download:
                await DownloadCloudCommand.ExecuteAsync(null);
                break;
        }
    }

    [RelayCommand]
    private async Task DownloadCloudWorldAsync(CloudWorldEntry? entry)
    {
        if (entry is null || IsBusy || IsBlockedByRunningGame("downloading a world"))
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Download shared world",
            $"Download {entry.WorldName} ({entry.VersionText}) to this PC?\n\nIt is added as a new world. No existing world is changed.");
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync($"Downloading {entry.WorldName}...", async () =>
        {
            var profile = (await _activeGame.Current.SaveAdapter.DiscoverSaveRootsAsync()).FirstOrDefault();
            if (profile is null)
            {
                _dialogService.ShowError($"No {ActiveGameName} save folder", $"Start {ActiveGameName} once and close it, then download again.");
                return;
            }

            var result = await _cloudSyncService.DownloadCloudWorldAsync(profile, entry.WorldId);
            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Download blocked", result.Message);
                return;
            }

            await RefreshAsync();
            SelectedWorld = Worlds.FirstOrDefault(world => string.Equals(world.WorldId, entry.WorldId, StringComparison.OrdinalIgnoreCase)) ?? SelectedWorld;

            StatusText = result.Message;
            AddActivity("Success", result.Message);
            _toastService.Success("World downloaded", result.Message);
            if (_activeGame.Current.PostRestoreHint is { } hint)
            {
                AddActivity("Info", hint);
            }
        });
    }
}
