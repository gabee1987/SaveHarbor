using System.IO;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        StartGameMonitor();
        await PromptCloudFolderSetupIfNeededAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync($"Scanning for {ActiveGameName} worlds...", async () =>
        {
            UpdateGameStatus();
            Worlds.Clear();

            var worlds = await _activeGame.Current.SaveAdapter.DiscoverWorldsAsync();
            foreach (var world in worlds)
            {
                Worlds.Add(world);
            }

            suppressSelectedWorldCloudRefresh = true;
            try
            {
                SelectedWorld ??= Worlds.FirstOrDefault();
            }
            finally
            {
                suppressSelectedWorldCloudRefresh = false;
            }

            await RefreshProfileStatusAsync();
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();
            StatusText = Worlds.Count == 0
                ? $"No {ActiveGameName} worlds found."
                : $"Found {Worlds.Count} world{(Worlds.Count == 1 ? string.Empty : "s")}.";

            AddActivity("Info", StatusText);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task CreateBackupAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning($"{ActiveGameName} is running", "Close the game before creating a backup.");
            _dialogService.ShowError($"{ActiveGameName} is running", $"Close {ActiveGameName} before creating a backup so the save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Creating backup...", async () =>
        {
            LastBackup = await _backupService.CreateBackupAsync(SelectedWorld, BackupReasons.Manual);
            await RefreshBackupStatsAsync();
            StatusText = $"Backup created: {LastBackup.FileName}";
            AddActivity("Success", StatusText);
            _toastService.Success("Backup created", LastBackup.FileName);
            _dialogService.ShowInfo("Backup created", $"Saved backup:\n{LastBackup.FilePath}");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task RestoreBackupAsync()
    {
        if (SelectedWorld is null || IsBlockedByRunningGame("restoring a backup"))
        {
            return;
        }

        var backupPath = _dialogService.SelectZipFile(_backupService.GetBackupRoot(_activeGame.Current.Id));
        if (backupPath is not null)
        {
            await RestoreFromBackupAsync(backupPath, Path.GetFileName(backupPath));
        }
    }

    // Restores one of the backups listed under the selected world.
    [RelayCommand]
    private async Task RestoreListedBackupAsync(WorldBackupItem? backup)
    {
        if (backup is null || SelectedWorld is null || IsBusy || IsBlockedByRunningGame("restoring a backup"))
        {
            return;
        }

        await RestoreFromBackupAsync(backup.FilePath, $"{backup.Reason}, {backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
    }

    private async Task RestoreFromBackupAsync(string backupPath, string backupDescription)
    {
        var world = SelectedWorld!;
        var confirmed = _dialogService.Confirm(
            "Restore backup",
            $"SaveHarbor will create a safety backup first, then replace this world:\n\n{world.WorldName}\n\nWith:\n{backupDescription}");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Restoring backup...", async () =>
        {
            await _backupService.RestoreBackupAsync(backupPath, world);
            await RefreshSelectedWorldFromDiskAsync();

            StatusText = "Backup restored successfully.";
            await RefreshBackupStatsAsync();
            AddActivity("Success", StatusText);
            _toastService.Success("Restore complete", "Backup restored and safety backup created.");
            _dialogService.ShowInfo("Restore complete", "The backup was restored. The copy it replaced was saved as a \"Before restore\" backup.");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private void OpenWorldFolder()
    {
        if (SelectedWorld is not null)
        {
            ExplorerLauncher.ShowFile(SelectedWorld.SavePath);
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        ExplorerLauncher.OpenFolder(_backupService.GetBackupRoot(_activeGame.Current.Id));
    }
}
