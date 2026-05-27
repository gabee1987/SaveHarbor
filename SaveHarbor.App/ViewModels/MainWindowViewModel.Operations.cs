using System.Diagnostics;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private bool HasSelectedWorld()
    {
        return SelectedWorld is not null && !IsBusy;
    }

    private bool IsNotBusy()
    {
        return !IsBusy;
    }

    private async Task RunBusyAsync(string busyText, Func<Task> action)
    {
        try
        {
            _logger.Debug(AppLogKeyword.Ui, "Starting operation: {Operation}", busyText);
            IsBusy = true;
            StatusText = busyText;
            NotifyCommandStates();
            await action();
            _logger.Debug(AppLogKeyword.Ui, "Completed operation: {Operation}", busyText);
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, busyText, AppLogKeyword.Ui);
            StatusText = "Operation failed.";
            AddActivity("Error", FormatActivityError(error));
            _toastService.Error("Operation failed", error.UserMessage);
            _dialogService.ShowError("SaveHarbor error", FormatDialogError(error));
        }
        finally
        {
            IsBusy = false;
            UpdateGameStatus();
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        CreateBackupCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
        OpenWorldFolderCommand.NotifyCanExecuteChanged();
        SetupCloudFolderCommand.NotifyCanExecuteChanged();
        ConnectCloudCommand.NotifyCanExecuteChanged();
        CheckCloudCommand.NotifyCanExecuteChanged();
        UploadCloudCommand.NotifyCanExecuteChanged();
        DownloadCloudCommand.NotifyCanExecuteChanged();
        StartCloudSessionCommand.NotifyCanExecuteChanged();
        EndCloudSessionCommand.NotifyCanExecuteChanged();
    }

    private void UpdateGameStatus()
    {
        IsGameRunning = _processDetectionService.IsWindroseRunning();
    }

    private async Task RefreshBackupStatsAsync()
    {
        var backups = await _backupService.ListBackupsAsync();
        BackupCount = backups.Count;
        TotalBackupSize = DisplayFormatter.FormatBytes(backups.Sum(backup => backup.SizeBytes));
        LastBackup = backups.FirstOrDefault();
    }

    private async Task RefreshProfileStatusAsync()
    {
        var profiles = await _saveDiscoveryService.DiscoverProfilesAsync();
        var profile = profiles.FirstOrDefault();

        ProfileStatus = profile is null
            ? "No Windrose profile found. Start Windrose once before importing a backup."
            : $"Profile {profile.ProfileId} • RocksDB {profile.RocksDbVersion}";
    }

    private async Task RefreshCloudStatusAsync(bool showToast)
    {
        if (SelectedWorld is null)
        {
            CloudStatus = null;
            return;
        }

        try
        {
            CloudStatus = await _cloudSyncService.RefreshStatusAsync(SelectedWorld);
            if (showToast)
            {
                _toastService.Info(CloudStatus.Title, CloudStatus.Detail);
                AddActivity("Info", $"{CloudStatus.Title}: {CloudStatus.Detail}");
            }
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Refresh cloud status", AppLogKeyword.CloudSync);
            AddActivity("Error", FormatActivityError(error));
            _toastService.Error("Cloud check failed", error.UserMessage);
        }
    }

    private async Task RefreshSelectedWorldFromDiskAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var refreshed = await _saveDiscoveryService.ReadWorldAsync(SelectedWorld.SavePath);
        if (refreshed is null)
        {
            return;
        }

        var index = Worlds.IndexOf(SelectedWorld);
        if (index >= 0)
        {
            Worlds[index] = refreshed;
        }

        suppressSelectedWorldCloudRefresh = true;
        try
        {
            SelectedWorld = refreshed;
        }
        finally
        {
            suppressSelectedWorldCloudRefresh = false;
        }
    }

    private void AddActivity(string level, string message)
    {
        Activity.Insert(0, new ActivityLogItem(DateTimeOffset.Now, level, message));
        while (Activity.Count > 12)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private static string FormatActivityError(AppError error)
    {
        return $"{error.Code} [{error.ErrorId}]: {error.UserMessage}";
    }

    private static string FormatDialogError(AppError error)
    {
        return $"{error.UserMessage}\n\nError ID: {error.ErrorId}\nCode: {error.Code}\nDetails: {error.TechnicalMessage}";
    }

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
