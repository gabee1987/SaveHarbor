using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

public enum SharingHeroState
{
    // Google Drive and the local test cloud keep their own status lines.
    None,
    ChooseProvider,
    NotConfigured,
    SignIn,
    Group,
    Connected,
    Unreachable,
    UpdateRequired
}

// The line at the top of the Sharing view: how this PC shares the active game's worlds, and the one action that moves
// it forward.
public partial class MainWindowViewModel
{
    // The active provider's connection as last checked; null until checked and for providers without the line.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharingHero), nameof(HasSharingHero), nameof(SharingHeroText), nameof(SharingActionText), nameof(HasSharingAction))]
    private CloudConnectionStatus? sharingConnection;

    private bool cloudGroupsListedThisRun;

    // A world shared by a newer app blocks every provider, Google Drive included.
    public SharingHeroState SharingHero => CloudStatus?.State == CloudSyncState.UpdateRequired
        ? SharingHeroState.UpdateRequired
        : ProviderSharingHero;

    private SharingHeroState ProviderSharingHero => _providerSelector.ActiveKind switch
    {
        "" => SharingHeroState.ChooseProvider,
        CloudProviderKind.Supabase => SharingConnection?.Reason switch
        {
            CloudConnectionReason.NotConfigured => SharingHeroState.NotConfigured,
            CloudConnectionReason.SignIn => SharingHeroState.SignIn,
            CloudConnectionReason.Group => SharingHeroState.Group,
            CloudConnectionReason.Unreachable => SharingHeroState.Unreachable,
            CloudConnectionReason.UpdateRequired => SharingHeroState.UpdateRequired,
            _ when !IsSaveHarborCloudAvailable => SharingHeroState.NotConfigured,
            _ when !_cloudGroups.IsSignedIn => SharingHeroState.SignIn,
            _ when !_cloudGroups.HasGroupConfigured(_activeGame.Current.Id) => SharingHeroState.Group,
            _ => SharingHeroState.Connected
        },
        _ => SharingHeroState.None
    };

    public bool HasSharingHero => SharingHero != SharingHeroState.None;

    public string SharingHeroText => SharingHero switch
    {
        SharingHeroState.ChooseProvider => "Choose how to share your worlds.",
        SharingHeroState.NotConfigured => "SaveHarbor Cloud is not set up in this version of the app.",
        SharingHeroState.SignIn => "Sign in to share with your group.",
        SharingHeroState.Group => $"Join or create a group for {ActiveGameName}.",
        SharingHeroState.Connected => DescribeConnectedGroup(),
        SharingHeroState.Unreachable => SharingConnection?.Message ?? string.Empty,
        SharingHeroState.UpdateRequired => "This version of SaveHarbor is too old for your group's cloud. Update to continue sharing.",
        _ => string.Empty
    };

    public string SharingActionText => SharingHero switch
    {
        SharingHeroState.ChooseProvider => "Choose",
        SharingHeroState.SignIn => "Sign in",
        SharingHeroState.Group => "Set up group",
        SharingHeroState.Connected => "Change",
        SharingHeroState.Unreachable => "Retry",
        SharingHeroState.UpdateRequired => "Update",
        _ => string.Empty
    };

    public bool HasSharingAction => SharingActionText.Length > 0;

    // "Change group" for SaveHarbor Cloud, "Change shared folder" otherwise.
    public string SetupCloudLinkText => _providerSelector.ActiveKind == CloudProviderKind.Supabase ? "Change group" : "Change shared folder";

    // A friend's lock: how long it still holds, or that it ran out and the world can be taken over.
    public string CloudLockText
    {
        get
        {
            if (CloudStatus?.SessionLock is not { } sessionLock || CloudStatus.IsOwnSession)
            {
                return string.Empty;
            }

            var now = DateTimeOffset.UtcNow;
            return sessionLock.IsLiveAt(now)
                ? $"Lock expires in {Math.Max(1, (int)Math.Ceiling((sessionLock.ExpiresAtUtc - now).TotalMinutes))} min"
                : $"{sessionLock.PlayerName}'s session expired at {sessionLock.ExpiresAtUtc.ToLocalTime():HH:mm} — you can take over";
        }
    }

    public bool HasCloudLockText => CloudLockText.Length > 0;

    private string DescribeConnectedGroup()
    {
        var groupId = _cloudGroups.GetActiveGroupId(_activeGame.Current.Id);
        var group = _cloudGroups.KnownGroups.FirstOrDefault(known => known.Id == groupId);
        var parts = new List<string> { group?.Name ?? "Your group" };
        if (group is { MemberCount: > 0 })
        {
            parts.Add(group.MemberCount == 1 ? "1 member" : $"{group.MemberCount} members");
        }

        if (_cloudGroups.SignedInEmail is { Length: > 0 } email)
        {
            parts.Add($"signed in as {email}");
        }

        return string.Join(" · ", parts);
    }

    // Reads the connection of SaveHarbor Cloud; the group list once per run, so member counts are current and groups
    // the player left elsewhere are dropped.
    private async Task RefreshSharingHeroAsync()
    {
        if (_providerSelector.ActiveKind != CloudProviderKind.Supabase || !IsSaveHarborCloudAvailable)
        {
            SharingConnection = null;
            NotifySharingHero();
            return;
        }

        try
        {
            if (!cloudGroupsListedThisRun && _cloudGroups.IsSignedIn)
            {
                await _cloudGroups.ListGroupsAsync();
                cloudGroupsListedThisRun = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(AppLogKeyword.CloudSync, "SaveHarbor Cloud groups could not be listed: {Message}", ex.Message);
        }

        try
        {
            SharingConnection = await _cloudSyncService.GetConnectionStatusAsync(_activeGame.Current.Id);
        }
        catch (Exception ex)
        {
            _logger.Warning(AppLogKeyword.CloudSync, "SaveHarbor Cloud status could not be read: {Message}", ex.Message);
            SharingConnection = null;
        }

        NotifySharingHero();
    }

    private void NotifySharingHero()
    {
        OnPropertyChanged(nameof(SharingHero));
        OnPropertyChanged(nameof(HasSharingHero));
        OnPropertyChanged(nameof(SharingHeroText));
        OnPropertyChanged(nameof(SharingActionText));
        OnPropertyChanged(nameof(HasSharingAction));
        OnPropertyChanged(nameof(SetupCloudLinkText));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RunSharingActionAsync()
    {
        switch (SharingHero)
        {
            case SharingHeroState.ChooseProvider:
                await SetupCloudAsync();
                break;
            case SharingHeroState.SignIn or SharingHeroState.Group or SharingHeroState.Connected:
                await SetupCloudGroupAsync();
                break;
            case SharingHeroState.Unreachable:
                await CheckCloudCommand.ExecuteAsync(null);
                break;
            case SharingHeroState.UpdateRequired:
                await CheckForUpdatesAsync();
                break;
        }
    }
}
