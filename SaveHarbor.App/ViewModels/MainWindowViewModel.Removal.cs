using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// Removing a world from this PC and removing it from the cloud are separate actions. Both keep a verified backup
// first and are confirmed with a warning that says exactly what is deleted and what is not.
public partial class MainWindowViewModel
{
    public bool CanRemoveSelectedFromCloud => SelectedWorldStatus?.LatestVersion is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task RemoveLocalWorldAsync()
    {
        if (SelectedWorld is not { } world || IsBlockedByRunningGame("removing a world"))
        {
            return;
        }

        if (IsOwnSession)
        {
            _dialogService.ShowError("End your session first", $"You are hosting {world.WorldName}. End the session in the Sharing tab before removing the world from this PC.");
            return;
        }

        var cloudNote = SelectedWorldStatus?.LatestVersion is { } latest
            ? $"The shared cloud copy (v{latest.VersionNumber}) is not touched; you can download it again at any time."
            : "This world is not in the cloud, so the backup will be its only copy.";
        var unsharedNote = SelectedWorldStatus?.HasLocalChanges == true
            ? "\n\nWARNING: You have played since your last upload. That progress will then exist only in the backup."
            : string.Empty;

        var confirmed = _dialogService.ConfirmDanger(
            $"Remove {world.WorldName} from this PC",
            $"SaveHarbor will first back up this world and check the backup, then delete its save from the game folder:\n{world.SavePath}\n\n{cloudNote}{unsharedNote}\n\nTo bring it back later: Backups tab → {world.WorldName} → Restore as world.",
            "Remove from PC");
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync($"Removing {world.WorldName} from this PC...", async () =>
        {
            var backup = await _backupService.RemoveWorldAsync(world);
            SelectedWorld = null;
            await RefreshAsync();

            StatusText = $"Removed {world.WorldName} from this PC. Backup: {backup.FileName}";
            AddActivity("Success", StatusText);
            _toastService.Success("Removed from this PC", $"{world.WorldName} is kept as backup {backup.FileName}.");
        });
    }

    // Without a parameter it removes the selected world's cloud copy; cloud-only rows pass their entry.
    [RelayCommand]
    private async Task RemoveCloudWorldAsync(CloudWorldEntry? entry)
    {
        if (IsBusy)
        {
            return;
        }

        var target = entry ?? SelectedWorldCloudEntry();
        if (target is null)
        {
            return;
        }

        var localWorld = Worlds.FirstOrDefault(world => string.Equals(world.WorldId, target.WorldId, StringComparison.OrdinalIgnoreCase));
        var localNote = localWorld is null
            ? "There is no copy of this world on this PC; after removal only your backup remains."
            : "Your copy on this PC is not touched. You can upload it again later; it then starts again at v1.";

        var confirmed = _dialogService.ConfirmDanger(
            $"Remove {target.WorldName} from the cloud",
            $"This removes {target.WorldName} and all of its shared versions from your group's shared folder, for everyone in the group.\n\nSaveHarbor first downloads the latest version ({target.VersionText}) and keeps it in your backups. Google Drive keeps the removed folder in its trash for 30 days.\n\n{localNote}",
            "Remove from cloud");
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync($"Removing {target.WorldName} from the cloud...", async () =>
        {
            var result = await _cloudSyncService.RemoveCloudWorldAsync(_activeGame.Current.Id, target.WorldId);
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();

            StatusText = result.Message;
            AddActivity(result.IsSuccess ? "Success" : "Warning", result.Message);
            if (result.IsSuccess)
            {
                _toastService.Success("Removed from the cloud", result.Message);
            }
            else
            {
                _toastService.Warning("Not removed from the cloud", result.Message);
            }
        });
    }

    private CloudWorldEntry? SelectedWorldCloudEntry() =>
        SelectedWorld is { } world && SelectedWorldStatus?.LatestVersion is { } latest
            ? new CloudWorldEntry(world.WorldId, world.WorldName, $"v{latest.VersionNumber}", string.Empty)
            : null;

    // A backup of any world, restored as that world: replaces it if it exists (after a safety backup), else adds it.
    [RelayCommand]
    private async Task RestoreBackupAsWorldAsync(WorldBackupItem? backup)
    {
        if (backup is null || IsBusy || IsBlockedByRunningGame("restoring a world"))
        {
            return;
        }

        await ImportFromBackupAsync(backup.FilePath);
    }

    [RelayCommand]
    private static void OpenFolderPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ExplorerLauncher.OpenExistingFolder(path);
        }
    }
}
