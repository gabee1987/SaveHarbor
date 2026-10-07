using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private async Task PromptCloudFolderSetupIfNeededAsync()
    {
        if (_cloudSetupService.HasSharedFolderConfigured(_activeGame.Current.Id))
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

        await SetupCloudFolderAsync();
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SetupCloudFolderAsync()
    {
        var game = _activeGame.Current;
        var choice = _dialogService.ConfigureCloudFolder(
            game.Id,
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

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ConnectCloudAsync()
    {
        await RunBusyAsync("Connecting cloud...", async () =>
        {
            var result = await _cloudSyncService.ConnectAsync(_activeGame.Current.Id);

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Cloud not connected", result.Message);
                return;
            }

            StatusText = result.Message;
            AddActivity("Success", result.Message);
            _toastService.Success("Cloud connected", result.Status.AccountEmail ?? result.Status.ProviderName);

            if (SelectedWorld is not null)
            {
                await RefreshCloudStatusAsync(showToast: false);
            }

            await RefreshCloudOverviewAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task CheckCloudAsync()
    {
        await RunBusyAsync("Checking cloud status...", async () =>
        {
            await RefreshCloudStatusAsync(showToast: true);
            await RefreshCloudOverviewAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task UploadCloudAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning($"{ActiveGameName} is running", "Close the game before uploading the world.");
            _dialogService.ShowError($"{ActiveGameName} is running", $"Close {ActiveGameName} before uploading so the save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Uploading current world...", async () =>
        {
            var result = await _cloudSyncService.UploadCurrentAsync(SelectedWorld);
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Upload blocked", result.Message);
                return;
            }

            StatusText = result.Message;
            AddActivity("Success", result.Message);
            _toastService.Success("Cloud upload complete", result.Message);
        });
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task DownloadCloudAsync()
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning($"{ActiveGameName} is running", "Close the game before downloading a cloud save.");
            _dialogService.ShowError($"{ActiveGameName} is running", $"Close {ActiveGameName} before downloading and restoring a cloud save.");
            return;
        }

        var confirmMessage = SelectedWorld is null
            ? $"Do you want to download the latest available cloud save into this computer's {ActiveGameName} profile?\n\nUse this after local worlds were deleted or on a fresh PC. {ActiveGameName} must be closed."
            : $"Do you want to download the latest cloud version of {SelectedWorld.WorldName}?\n\nSaveHarbor will first create a local safety backup of your current world, then restore the latest cloud save over this local world.\n\nChoose Continue to download and restore.\nChoose Cancel to leave your local world unchanged.";

        if (SelectedWorld is not null && CloudStatus?.HasLocalChanges == true)
        {
            confirmMessage = $"WARNING: You have played {SelectedWorld.WorldName} since the last upload or download, and that progress is not in the cloud. Downloading replaces it. It is kept as a \"Before restore\" backup.\n\n{confirmMessage}";
        }

        var confirmed = _dialogService.Confirm("Download latest cloud save", confirmMessage);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Downloading latest cloud save...", async () =>
        {
            var result = SelectedWorld is null
                ? await DownloadCloudWithoutLocalWorldAsync()
                : await _cloudSyncService.DownloadLatestAsync(SelectedWorld);

            await RefreshBackupStatsAsync();

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Download blocked", result.Message);
                return;
            }

            if (SelectedWorld is null)
            {
                await RefreshAsync();
            }
            else
            {
                await RefreshSelectedWorldFromDiskAsync();
                await RefreshCloudStatusAsync(showToast: false);
                await RefreshCloudOverviewAsync();
            }

            StatusText = result.Message;
            AddActivity("Success", result.Message);
            _toastService.Success("Cloud download complete", result.Message);

            if (_activeGame.Current.PostRestoreHint is { } hint)
            {
                AddActivity("Info", hint);
            }
        });
    }

    private async Task<CloudSyncResult> DownloadCloudWithoutLocalWorldAsync()
    {
        var profiles = await _activeGame.Current.SaveAdapter.DiscoverSaveRootsAsync();
        var profile = profiles.FirstOrDefault();
        if (profile is null)
        {
            return new CloudSyncResult(
                false,
                CloudSyncState.Error,
                $"No {ActiveGameName} profile was found. Start {ActiveGameName} once, close it, then download again.");
        }

        return await _cloudSyncService.DownloadLatestAvailableAsync(profile);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task StartCloudSessionAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        await RunBusyAsync("Starting cloud session...", async () =>
        {
            await TryStartCloudSessionAsync(SelectedWorld);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task EndCloudSessionAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        await RunBusyAsync("Ending cloud session...", async () =>
        {
            var result = await _cloudSyncService.EndSessionAsync(SelectedWorld);
            await RefreshCloudStatusAsync(showToast: false);

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Session not ended", result.Message);
                return;
            }

            StatusText = result.Message;
            AddActivity("Info", result.Message);
            _toastService.Success("Session ended", result.Message);
        });
    }

    private async Task<bool> TryStartCloudSessionAsync(GameWorld world)
    {
        var result = await _cloudSyncService.StartSessionAsync(world);
        await RefreshCloudStatusAsync(showToast: false);

        if (!result.IsSuccess)
        {
            StatusText = result.Message;
            AddActivity("Warning", result.Message);
            _toastService.Warning("Session not started", result.Message);
            return false;
        }

        StatusText = result.Message;
        AddActivity("Info", result.Message);
        hasObservedGameRunningDuringSession = IsGameRunning;

        if (result.Message.Contains("already active", StringComparison.OrdinalIgnoreCase))
        {
            _toastService.Info("Session already active", result.Message);
        }
        else
        {
            _toastService.Success("Session started", result.Message);
        }

        return true;
    }
}
