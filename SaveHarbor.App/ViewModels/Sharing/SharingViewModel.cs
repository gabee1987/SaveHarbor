using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

// The Sharing page of Settings: everything about sharing worlds with friends in one place. It opens on what the player
// needs next (choose a provider, sign in, pick a group) and otherwise on the group itself.
public sealed partial class SharingViewModel : ObservableObject
{
    private readonly SharingServices _services;
    private readonly GameId _game;
    private readonly TimeProvider? _time;

    public SharingViewModel(SharingServices services, GameId game, string gameName, TimeProvider? time = null)
    {
        _services = services;
        _game = game;
        _time = time;
        GameName = gameName;
        Profile = new SharingProfileViewModel(services);
        Group = new SharingGroupViewModel(services, game, gameName);
        Profile.DisplayNameShared += async (_, _) => await ReloadMembersAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsSignIn), nameof(IsGroup), nameof(IsDrive), nameof(Heading), nameof(Subheading))]
    [NotifyPropertyChangedFor(nameof(CanSwitchToDrive), nameof(CanSwitchToCloud))]
    private SharingScreen screen;

    [ObservableProperty]
    private CloudOnboardingViewModel? onboarding;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string driveFolderText = string.Empty;

    public string GameName { get; }

    public SharingProfileViewModel Profile { get; }

    public SharingGroupViewModel Group { get; }

    public bool IsWelcome => Screen == SharingScreen.Welcome;

    public bool IsSignIn => Screen == SharingScreen.SignIn;

    public bool IsGroup => Screen == SharingScreen.Group;

    public bool IsDrive => Screen == SharingScreen.Drive;

    public bool IsCloudAvailable => _services.Selector.AvailableKinds.Contains(CloudProviderKind.Supabase);

    public bool IsDriveAvailable => _services.Selector.AvailableKinds.Contains(CloudProviderKind.GoogleDrive);

    public bool CanSwitchToDrive => _services.Selector.ActiveKind == CloudProviderKind.Supabase && IsDriveAvailable;

    public bool CanSwitchToCloud => _services.Selector.ActiveKind == CloudProviderKind.GoogleDrive && IsCloudAvailable;

    public string Heading => Screen switch
    {
        SharingScreen.Welcome => "Share worlds with your friends",
        SharingScreen.SignIn => "SaveHarbor Cloud",
        SharingScreen.Drive => "Google Drive",
        _ => Group.GroupName
    };

    public string Subheading => Screen switch
    {
        SharingScreen.Welcome => "Pick how your group keeps its worlds. You can switch later.",
        SharingScreen.SignIn => $"Sign in once and play {GameName} worlds with your group.",
        SharingScreen.Drive => $"Your group shares {GameName} worlds through a Google Drive folder.",
        _ => Group.RoleLine
    };

    // "Decide later" on the provider choice: Settings closes, and the main window does not offer it again this run.
    public bool Postponed { get; private set; }

    // The group joined, created or switched to while the page was open, for the main window's toast.
    public string? ConnectedGroupName { get; private set; }

    // An invite code from a saveharbor://join link: the next sign-in screen opens on joining with it filled in.
    public string? InviteFromLink { get; set; }

    // "Decide later": the page asks Settings to close.
    public event EventHandler? CloseRequested;

    public Task InitializeAsync() => ShowCurrentAsync();

    private async Task ShowCurrentAsync()
    {
        switch (_services.Selector.ActiveKind)
        {
            case CloudProviderKind.Supabase when IsCloudAvailable:
                if (_services.Groups.IsSignedIn && _services.Groups.HasGroupConfigured(_game) && InviteFromLink is null)
                {
                    await ShowGroupAsync();
                }
                else
                {
                    await StartOnboardingAsync(null);
                }

                break;
            case CloudProviderKind.GoogleDrive or CloudProviderKind.LocalTest:
                ShowDrive();
                break;
            default:
                Screen = SharingScreen.Welcome;
                break;
        }
    }

    private async Task StartOnboardingAsync(GroupSetupMode? mode)
    {
        if (Onboarding is { } previous)
        {
            previous.Completed -= OnOnboardingCompleted;
        }

        var otherGameHasGroup = Enum.GetValues<GameId>().Any(other => other != _game && _services.Groups.HasGroupConfigured(other));
        var onboarding = new CloudOnboardingViewModel(
            _services.Groups, _services.Player, _services.ErrorHandler, _game, otherGameHasGroup, Profile.Save, _time);
        onboarding.Completed += OnOnboardingCompleted;
        if (InviteFromLink is { } invite)
        {
            onboarding.InviteCode = invite;
            mode = GroupSetupMode.Join;
            InviteFromLink = null;
        }

        if (mode is { } groupMode && _services.Groups.IsSignedIn)
        {
            onboarding.ShowGroupStep(groupMode);
        }

        Onboarding = onboarding;
        Screen = SharingScreen.SignIn;
        await onboarding.InitializeAsync();
    }

    private async void OnOnboardingCompleted(object? sender, EventArgs e)
    {
        if (sender is not CloudOnboardingViewModel { Result: { } result } onboarding)
        {
            return;
        }

        try
        {
            ConnectedGroupName = result.GroupName;
            await ShowGroupAsync();
            if (onboarding.CreatedInviteCode.Length > 0)
            {
                Group.InviteCode = onboarding.CreatedInviteCode;
            }
        }
        catch (Exception exception)
        {
            _services.ErrorHandler.Handle(exception, "Show your group", AppLogKeyword.Ui);
        }
    }

    private async Task ShowGroupAsync()
    {
        Group.Reset();
        Profile.Refresh();
        Screen = SharingScreen.Group;
        await Group.LoadAsync();
        NotifyHeading();
    }

    private void ShowDrive()
    {
        var folderId = _services.Folders.GetCurrentSharedFolderId(_game);
        var name = _services.Folders.GetSavedFolders(_game).FirstOrDefault(folder => folder.FolderId == folderId)?.Name;
        DriveFolderText = _services.Selector.ActiveKind == CloudProviderKind.LocalTest
            ? "Developer test cloud on this PC."
            : !_services.Folders.HasSharedFolderConfigured(_game)
                ? "No shared folder chosen yet."
                : name is { Length: > 0 } ? name : "Shared folder set up";
        Profile.Refresh();
        Screen = SharingScreen.Drive;
    }

    private async Task ReloadMembersAsync()
    {
        try
        {
            await Group.LoadMembersAsync();
        }
        catch (Exception exception)
        {
            _services.ErrorHandler.Handle(exception, "Read your group", AppLogKeyword.CloudSync);
        }
    }

    private void NotifyHeading()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subheading));
        OnPropertyChanged(nameof(CanSwitchToDrive));
        OnPropertyChanged(nameof(CanSwitchToCloud));
    }
}
