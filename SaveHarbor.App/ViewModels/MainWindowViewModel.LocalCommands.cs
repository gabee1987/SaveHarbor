using System.IO;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    public async Task InitializeAsync()
    {
        await RefreshAsync();
        await PromptCloudFolderSetupIfNeededAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("Scanning for Windrose worlds...", async () =>
        {
            UpdateGameStatus();
            Worlds.Clear();

            var worlds = await _saveDiscoveryService.DiscoverWorldsAsync();
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
            StatusText = Worlds.Count == 0
                ? "No Windrose worlds found."
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
            _toastService.Warning("Windrose is running", "Close the game before creating a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before creating a backup so the RocksDB save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Creating backup...", async () =>
        {
            LastBackup = await _backupService.CreateBackupAsync(SelectedWorld, "manual");
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
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before restoring a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before restoring a backup.");
            return;
        }

        var backupPath = _dialogService.SelectZipFile(_backupService.BackupRoot);
        if (backupPath is null)
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Restore backup",
            $"SaveHarbor will create a safety backup first, then replace this world:\n\n{SelectedWorld.WorldName}\n\nWith:\n{Path.GetFileName(backupPath)}");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Restoring backup...", async () =>
        {
            await _backupService.RestoreBackupAsync(backupPath, SelectedWorld);
            await RefreshSelectedWorldFromDiskAsync();

            StatusText = "Backup restored successfully.";
            await RefreshBackupStatsAsync();
            AddActivity("Success", StatusText);
            _toastService.Success("Restore complete", "Backup restored and safety backup created.");
            _dialogService.ShowInfo("Restore complete", "The backup was restored and a pre-restore safety backup was created.");
        });
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before importing a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before importing a world backup.");
            return;
        }

        var backupPath = _dialogService.SelectZipFile(_backupService.BackupRoot);
        if (backupPath is null)
        {
            return;
        }

        BackupManifest manifest;
        IReadOnlyList<WindroseProfile> profiles;

        try
        {
            manifest = await _backupService.ReadManifestAsync(backupPath);
            profiles = await _saveDiscoveryService.DiscoverProfilesAsync();
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Read import backup", AppLogKeyword.Import);
            _toastService.Error("Cannot import backup", error.UserMessage);
            _dialogService.ShowError("Cannot import backup", FormatDialogError(error));
            AddActivity("Error", FormatActivityError(error));
            return;
        }

        var profile = profiles.FirstOrDefault();
        if (profile is null)
        {
            _toastService.Warning("Profile not found", "Start Windrose once, close it, then import again.");
            _dialogService.ShowError(
                "Windrose profile not found",
                "Start Windrose once on this computer, let it reach the main menu or create its local profile, then close it and try importing again.");
            AddActivity("Error", "Import blocked: Windrose profile not found.");
            return;
        }

        var targetWorldPath = Path.Combine(profile.WorldsPath, manifest.WorldId);
        var worldExists = Directory.Exists(targetWorldPath);
        var actionText = worldExists
            ? "This world already exists on this computer. Importing will replace that local world folder."
            : "This will add the world to this computer.";

        var confirmed = _dialogService.Confirm(
            "Import world backup",
            $"World: {manifest.WorldName}\nWorld ID: {manifest.WorldId}\nProfile: {profile.ProfileId}\n\n{actionText}\n\nKeep Windrose closed while importing.");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Importing backup as a new world...", async () =>
        {
            var importedPath = await _backupService.ImportBackupAsNewWorldAsync(backupPath, profile, overwriteExisting: worldExists);
            await RefreshBackupStatsAsync();
            await RefreshProfileStatusAsync();

            var importedWorld = await _saveDiscoveryService.ReadWorldAsync(importedPath);
            await RefreshAsync();
            if (importedWorld is not null)
            {
                SelectedWorld = Worlds.FirstOrDefault(world =>
                    string.Equals(world.WorldId, importedWorld.WorldId, StringComparison.OrdinalIgnoreCase));
            }

            StatusText = $"Imported world backup: {manifest.WorldName}";
            AddActivity("Success", StatusText);
            _toastService.Success("Import complete", manifest.WorldName);
            _dialogService.ShowInfo("Import complete", $"Imported {manifest.WorldName}.\n\nStart Windrose and check that the world appears.");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private void OpenWorldFolder()
    {
        if (SelectedWorld is not null)
        {
            OpenFolder(SelectedWorld.SavePath);
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(_backupService.BackupRoot);
        OpenFolder(_backupService.BackupRoot);
    }
}
