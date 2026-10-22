using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

// Reads the cloud for the whole window at once: the Sharing line, the world list and the selected world. The last
// result is cached, so the next start shows it at once, marked with its time, until the live one arrives.
public partial class MainWindowViewModel
{
    private static readonly TimeSpan CloudRefreshDelay = TimeSpan.FromMilliseconds(250);

    // A refresh waiting to start: requests made meanwhile join it, so a command's refreshes cost one snapshot.
    private Task? queuedCloudRefresh;

    // Only the newest refresh is shown; one that started earlier but answered later is dropped.
    private int cloudRefreshGeneration;

    private bool hasLiveCloudOverview;

    // When the cached world list being shown was read; null once the live one is shown.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudStale), nameof(CloudStaleText), nameof(PlayPreflightText), nameof(HasPlayPreflight))]
    private DateTimeOffset? cloudStaleSince;

    public bool IsCloudStale => CloudStaleSince is not null;

    public string CloudStaleText => CloudStaleSince is { } at ? $"as of {at.ToLocalTime():HH:mm}" : string.Empty;

    private Task RefreshCloudAsync(string reason) => queuedCloudRefresh ??= RunQueuedCloudRefreshAsync(reason);

    private async Task RunQueuedCloudRefreshAsync(string reason)
    {
        await Task.Delay(CloudRefreshDelay);
        queuedCloudRefresh = null;
        await RefreshCloudNowAsync(reason);
    }

    private async Task RefreshCloudNowAsync(string reason)
    {
        var game = _activeGame.Current.Id;
        var selected = SelectedWorld;
        var generation = ++cloudRefreshGeneration;
        var stopwatch = Stopwatch.StartNew();
        await ListCloudGroupsOnceAsync();

        try
        {
            var refresh = await _cloudSyncService.RefreshAllAsync(game, Worlds.ToArray(), selected);
            if (generation != cloudRefreshGeneration || game != _activeGame.Current.Id)
            {
                return;
            }

            SharingConnection = ShowsSharingConnection ? refresh.Connection : null;
            NotifySharingHero();
            // A world selected meanwhile reads its own status.
            if (selected is null ? SelectedWorld is null : IsSameWorld(selected, SelectedWorld))
            {
                CloudStatus = refresh.SelectedStatus;
            }

            ApplyCloudOverview(refresh.Overview);
            hasLiveCloudOverview = true;
            CloudStaleSince = null;
            CacheCloudOverview(game, refresh);
            _logger.Debug(AppLogKeyword.Ui, "Cloud refresh ({Reason}) took {ElapsedMs} ms", reason, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Refresh cloud status", AppLogKeyword.CloudSync);
            AddActivity("Error", FormatActivityError(error));
            _toastService.Error("Cloud check failed", error.UserMessage);
        }
    }

    private static bool IsSameWorld(GameWorld first, GameWorld? second) =>
        second is not null && first.Game == second.Game && string.Equals(first.WorldId, second.WorldId, StringComparison.OrdinalIgnoreCase);

    private void ApplyCloudOverview(CloudWorldOverview overview)
    {
        WorldSyncBadges = overview.LocalWorlds
            .Select(summary => (summary.WorldId, Badge: SyncAdvisor.Badge(summary.State, summary.HasLocalChanges)))
            .Where(item => item.Badge is not null)
            .ToDictionary(item => item.WorldId, item => item.Badge!, StringComparer.OrdinalIgnoreCase);

        CloudOnlyWorlds.Clear();
        foreach (var manifest in overview.CloudOnlyWorlds)
        {
            CloudOnlyWorlds.Add(CloudWorldEntry.From(manifest));
        }

        worldSummaries = overview.LocalWorlds.ToDictionary(summary => summary.WorldId, StringComparer.OrdinalIgnoreCase);
        ApplySelectedStatusToBadges(CloudStatus);
        NotifyPlayPreflight();
    }

    // Right after a scan, before the cloud answers: the world list as the last run saw it.
    private void ShowCachedCloudOverview()
    {
        var game = _activeGame.Current.Id;
        if (hasLiveCloudOverview || CloudCacheScope(game) is not { Length: > 0 } scope)
        {
            return;
        }

        if (_cloudCache.Load(CloudProviderKind.ToStorageKey(_providerSelector.ActiveKind), game, scope) is { Overview.IsConnected: true } cached)
        {
            ApplyCloudOverview(cached.Overview);
            CloudStaleSince = cached.CachedAtUtc;
        }
    }

    private void CacheCloudOverview(GameId game, CloudRefresh refresh)
    {
        var providerKey = CloudProviderKind.ToStorageKey(_providerSelector.ActiveKind);
        if (refresh.Overview.IsConnected && CloudCacheScope(game) is { Length: > 0 } scope)
        {
            _cloudCache.Save(providerKey, game, scope, refresh.Overview, DateTimeOffset.UtcNow);
        }
        else if (refresh.Connection.Reason is not (CloudConnectionReason.Unreachable or CloudConnectionReason.UpdateRequired))
        {
            // Signed out, no group or no folder: the group's worlds do not stay on this PC.
            _cloudCache.Remove(providerKey, game);
        }
    }

    // The group or folder the cache belongs to; empty when there is none, so nothing is cached.
    private string CloudCacheScope(GameId game) => _providerSelector.ActiveKind switch
    {
        CloudProviderKind.Supabase => _cloudGroups.IsSignedIn ? _cloudGroups.GetActiveGroupId(game) ?? string.Empty : string.Empty,
        CloudProviderKind.LocalTest => "local",
        CloudProviderKind.GoogleDrive => _cloudSetupService.GetCurrentSharedFolderId(game),
        _ => string.Empty
    };
}
