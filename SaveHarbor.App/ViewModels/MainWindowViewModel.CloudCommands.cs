using CommunityToolkit.Mvvm.Input;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task CheckCloudAsync()
    {
        await RefreshCloudStatusAsync(showToast: true);
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
            var result = await _cloudSyncService.StartSessionAsync(SelectedWorld);
            await RefreshCloudStatusAsync(showToast: false);

            if (!result.IsSuccess)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Session not started", result.Message);
                return;
            }

            StatusText = result.Message;
            AddActivity("Info", result.Message);
            if (result.Message.Contains("already active", StringComparison.OrdinalIgnoreCase))
            {
                _toastService.Info("Session already active", result.Message);
            }
            else
            {
                _toastService.Success("Session started", result.Message);
            }
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
}
