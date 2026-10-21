using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    // "Decide later" in the provider choice, and the games whose group dialog was already offered, this run.
    private bool cloudChoicePostponed;
    private readonly HashSet<GameId> cloudGroupPrompted = [];

    // How sharing stood when Settings opened, so closing it reads the cloud again only after a change.
    private (string Provider, bool SignedIn, string? Group) sharingOpenedWith;

    // First start and game switch: open Settings on the Sharing page with what is missing (the provider choice, or the
    // SaveHarbor Cloud sign-in and group), once per run; afterwards the Sharing view says what is missing.
    private async Task PromptCloudSetupIfNeededAsync()
    {
        var game = _activeGame.Current.Id;
        if (_providerSelector.ActiveKind == CloudProviderKind.LocalTest)
        {
            return;
        }

        if (_providerSelector.ActiveKind.Length == 0 && IsSaveHarborCloudAvailable)
        {
            if (!cloudChoicePostponed)
            {
                await OpenSharingAsync();
            }

            return;
        }

        if (_providerSelector.ActiveKind == CloudProviderKind.Supabase)
        {
            if (IsSaveHarborCloudAvailable && !IsCloudGroupReady(game) && cloudGroupPrompted.Add(game))
            {
                await OpenSharingAsync();
            }

            return;
        }

        if (_cloudSetupService.HasSharedFolderConfigured(game))
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Set up cloud sync",
            "Google Drive sync needs one shared folder for your group.\n\nPaste and test the shared folder link now, or cancel and use the Setup button later.");

        if (!confirmed)
        {
            return;
        }

        await SetupSharedFolderAsync();
    }

    private bool IsSaveHarborCloudAvailable => _providerSelector.AvailableKinds.Contains(CloudProviderKind.Supabase);

    private bool IsCloudGroupReady(GameId game) => _cloudGroups.IsSignedIn && _cloudGroups.HasGroupConfigured(game);

    private async void OnCloudProviderChanged(object? sender, EventArgs e)
    {
        try
        {
            CloudStatus = null;
            ClearCloudOverview();
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();
            Settings?.Refresh();
        }
        catch (Exception ex)
        {
            _errorHandler.Handle(ex, "Switch cloud provider", AppLogKeyword.Ui);
        }
    }

    // Everything about sharing is on the Sharing page of Settings. The main window reads the cloud again when Settings
    // closes.
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task SetupCloudAsync() => OpenSharingAsync();

    private Task OpenSharingAsync() => ShowSettingsAsync(sharingPage: true);

    private SharingViewModel CreateSharing()
    {
        var game = _activeGame.Current;
        cloudGroupPrompted.Add(game.Id);
        sharingOpenedWith = SharingState();
        var sharing = new SharingViewModel(
            new SharingServices(
                _providerSelector,
                _cloudGroups,
                _cloudSyncService,
                _cloudSetupService,
                _deviceIdentity,
                _settingsStore,
                _playerIdentity,
                _errorHandler,
                _dialogService,
                _toastService,
                SetupSharedFolderAsync),
            game.Id,
            game.DisplayName);
        sharing.CloseRequested += (_, _) => IsSettingsOpen = false;
        return sharing;
    }

    // How this PC shares the active game: the provider, the sign-in and the group.
    private (string Provider, bool SignedIn, string? Group) SharingState() =>
        (_providerSelector.ActiveKind, _cloudGroups.IsSignedIn, _cloudGroups.GetActiveGroupId(_activeGame.Current.Id));

    private async Task AfterSharingAsync(SharingViewModel sharing)
    {
        cloudChoicePostponed |= sharing.Postponed;
        OnPropertyChanged(nameof(PlayerDisplayName));
        if (sharing.ConnectedGroupName is null && SharingState() == sharingOpenedWith)
        {
            return;
        }

        await RunBusyAsync("Checking the cloud...", async () =>
        {
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();
            Settings?.Refresh();
            OnPropertyChanged(nameof(PlayerDisplayName));

            if (sharing.ConnectedGroupName is not { } groupName)
            {
                return;
            }

            StatusText = $"Sharing with {groupName}.";
            AddActivity("Success", StatusText);
            _toastService.Success("SaveHarbor Cloud", $"Sharing with {groupName}");
        });
    }

    private async Task SetupSharedFolderAsync()
    {
        // A shared folder is the Google Drive way of sharing, so setting one up before any provider is chosen
        // chooses Google Drive.
        if (_providerSelector.ActiveKind.Length == 0)
        {
            await _providerSelector.SwitchAsync(CloudProviderKind.GoogleDrive);
        }

        var game = _activeGame.Current;
        var choice = _dialogService.ConfigureCloudFolder(
            game.SkinDictionary,
            game.DisplayName,
            _cloudSetupService.GetCurrentSharedFolderId(game.Id),
            _cloudSetupService.GetSavedFolders(game.Id),
            (candidate, cancellationToken) => _cloudSetupService.TestSharedFolderAsync(game.Id, candidate, cancellationToken),
            folderId => _cloudSetupService.ForgetSavedFolderAsync(game.Id, folderId));

        if (choice is null)
        {
            return;
        }

        await RunBusyAsync("Saving cloud folder setup...", async () =>
        {
            await _cloudSetupService.SaveSharedFolderAsync(game.Id, choice.Input, choice.FolderName);
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();

            StatusText = "Cloud folder setup saved.";
            AddActivity("Success", StatusText);
            _toastService.Success("Cloud folder saved", "SaveHarbor will use this shared Google Drive folder.");
        });
    }
}
