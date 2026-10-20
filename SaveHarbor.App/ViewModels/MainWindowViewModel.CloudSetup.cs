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

    // First start and game switch: offer the provider choice, then the setup the chosen provider needs. The choice
    // and the group dialog are offered once per run; afterwards the Sharing view says what is missing.
    private async Task PromptCloudSetupIfNeededAsync()
    {
        var game = _activeGame.Current.Id;
        if (_providerSelector.ActiveKind == CloudProviderKind.LocalTest)
        {
            return;
        }

        if (_providerSelector.ActiveKind.Length == 0 && IsSaveHarborCloudAvailable)
        {
            if (cloudChoicePostponed || !await ChooseCloudProviderAsync())
            {
                return;
            }
        }

        if (_providerSelector.ActiveKind == CloudProviderKind.Supabase)
        {
            if (IsSaveHarborCloudAvailable && !IsCloudGroupReady(game) && cloudGroupPrompted.Add(game))
            {
                await SetupCloudGroupAsync();
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

    // The setup of the active provider: group for SaveHarbor Cloud, shared folder for Google Drive. With no provider
    // chosen yet, the choice comes first.
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SetupCloudAsync()
    {
        if (_providerSelector.ActiveKind.Length == 0 && IsSaveHarborCloudAvailable && !await ChooseCloudProviderAsync())
        {
            return;
        }

        if (_providerSelector.ActiveKind == CloudProviderKind.Supabase)
        {
            await SetupCloudGroupAsync();
        }
        else
        {
            await SetupSharedFolderAsync();
        }
    }

    // False for "Decide later". Switching raises ActiveChanged, which refreshes the cloud views.
    private async Task<bool> ChooseCloudProviderAsync()
    {
        var choices = _providerSelector.AvailableKinds
            .Select(kind => new CloudProviderChoice(kind, kind == CloudProviderKind.Supabase))
            .ToArray();
        var kind = _dialogService.ChooseCloudProvider(_activeGame.Current.SkinDictionary, choices);
        if (kind is null)
        {
            cloudChoicePostponed = true;
            return false;
        }

        await _providerSelector.SwitchAsync(kind);
        AddActivity("Info", $"Sharing through {(kind == CloudProviderKind.Supabase ? "SaveHarbor Cloud" : "Google Drive")}.");
        return true;
    }

    private async Task SetupCloudGroupAsync()
    {
        var game = _activeGame.Current;
        var otherGameHasGroup = Enum.GetValues<GameId>().Any(other => other != game.Id && _cloudGroups.HasGroupConfigured(other));
        var setup = new CloudGroupSetupViewModel(_cloudGroups, _playerIdentity, _errorHandler, game.Id, otherGameHasGroup);
        var result = _dialogService.ConfigureCloudGroup(game.SkinDictionary, game.DisplayName, setup);

        // Signing in or out counts even when no group was chosen.
        await RunBusyAsync("Checking SaveHarbor Cloud...", async () =>
        {
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();
            Settings?.Refresh();

            if (result is null)
            {
                return;
            }

            StatusText = $"Connected to {result.GroupName}.";
            AddActivity("Success", StatusText);
            _toastService.Success("SaveHarbor Cloud", $"Connected to {result.GroupName}");
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
