using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private async Task PromptCloudFolderSetupIfNeededAsync()
    {
        if (_cloudSetupService.HasSharedFolderConfigured)
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
        var input = _dialogService.ConfigureCloudFolder(
            _cloudSetupService.CurrentSharedFolderId,
            _cloudSetupService.TestSharedFolderAsync);

        if (input is null)
        {
            return;
        }

        await RunBusyAsync("Saving cloud folder setup...", async () =>
        {
            await _cloudSetupService.SaveSharedFolderAsync(input);
            await RefreshCloudStatusAsync(showToast: false);

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
            var result = await _cloudSyncService.ConnectAsync();

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
        });
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task CheckCloudAsync()
    {
        await RunBusyAsync("Checking cloud status...", async () =>
        {
            await RefreshCloudStatusAsync(showToast: true);
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
            _toastService.Warning("Windrose is running", "Close the game before uploading the world.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before uploading so the RocksDB save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Uploading current world...", async () =>
        {
            var result = await _cloudSyncService.UploadCurrentAsync(SelectedWorld);
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);

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

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task DownloadCloudAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before downloading a cloud save.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before downloading and restoring a cloud save.");
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Download latest cloud save",
            $"Do you want to download the latest cloud version of {SelectedWorld.WorldName}?\n\nSaveHarbor will first create a local safety backup of your current world, then restore the latest cloud save over this local world.\n\nChoose Continue to download and restore.\nChoose Cancel to leave your local world unchanged.");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Downloading latest cloud save...", async () =>
        {
            var result = await _cloudSyncService.DownloadLatestAsync(SelectedWorld);
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Download blocked", result.Message);
                return;
            }

            await RefreshSelectedWorldFromDiskAsync();

            StatusText = result.Message;
            AddActivity("Success", result.Message);
            _toastService.Success("Cloud download complete", result.Message);
        });
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
    private async Task StartGameAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            await RunBusyAsync("Starting session for running Windrose...", async () =>
            {
                var sessionStarted = await TryStartCloudSessionAsync(SelectedWorld);
                if (!sessionStarted)
                {
                    return;
                }

                hasObservedGameRunningDuringSession = true;
                StatusText = "Windrose is running. Session is active.";
                AddActivity("Info", StatusText);
                _toastService.Success("Session active", "Windrose is already running, so SaveHarbor will end the session when the game closes.");
            });
            return;
        }

        await RunBusyAsync("Starting session and launching Windrose...", async () =>
        {
            var sessionStarted = await TryStartCloudSessionAsync(SelectedWorld);
            if (!sessionStarted)
            {
                return;
            }

            var launchResult = await _gameLauncherService.LaunchAsync();
            StatusText = launchResult.Message;

            if (!launchResult.IsSuccess)
            {
                AddActivity("Warning", launchResult.Message);
                _toastService.Warning("Launch failed", launchResult.Message);
                _dialogService.ShowError("Launch failed", launchResult.Message);
                return;
            }

            AddActivity("Success", "Session active. Windrose launch requested.");
            _toastService.Success("Starting Windrose", "Session is active and Steam has been asked to launch Windrose.");
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

    private async Task<bool> TryStartCloudSessionAsync(WindroseWorld world)
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
