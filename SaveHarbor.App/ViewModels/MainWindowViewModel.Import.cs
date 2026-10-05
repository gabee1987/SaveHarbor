using System.IO;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// Import accepts a SaveHarbor backup (.zip) or a world save from anywhere (a friend's file, the game's own backup
// copy). Nothing is changed before the user has seen what the file contains and how it compares to their copy.
public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        if (IsBlockedByRunningGame("importing a world"))
        {
            return;
        }

        var path = _dialogService.SelectImportFile(_backupService.GetBackupRoot(_activeGame.Current.Id), _activeGame.Current.SaveAdapter.ImportFileFilter);
        if (path is null)
        {
            return;
        }

        if (path.EndsWith(BackupFileName.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await ImportFromBackupAsync(path);
        }
        else
        {
            await ImportSaveFileAsync(path);
        }
    }

    private async Task ImportSaveFileAsync(string path)
    {
        if (IsBlockedByRunningGame("importing a world"))
        {
            return;
        }

        var adapter = _activeGame.Current.SaveAdapter;
        ImportCandidate? candidate;
        GameSaveRoot? profile;
        try
        {
            candidate = await adapter.ReadImportCandidateAsync(path);
            profile = await FindSaveRootAsync();
        }
        catch (Exception ex)
        {
            ReportImportError(ex);
            return;
        }

        if (candidate is null)
        {
            _dialogService.ShowError($"Not a {ActiveGameName} world", $"SaveHarbor could not read this file as a {ActiveGameName} world save. Nothing was changed.");
            return;
        }

        if (profile is null)
        {
            return;
        }

        var targetPath = adapter.GetExpectedWorldPath(profile, candidate.World.WorldId);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(candidate.World.SavePath), StringComparison.OrdinalIgnoreCase))
        {
            _dialogService.ShowInfo("Already your world", "This file is the world in your save folder, so there is nothing to import.");
            return;
        }

        var localExists = File.Exists(targetPath) || Directory.Exists(targetPath);
        var local = localExists ? await adapter.ReadImportCandidateAsync(targetPath) : null;
        var verdict = ImportComparison.Describe(candidate, local, localExists);
        var summary = string.Join("\n", candidate.Summary.Select(item => $"{item.Label}: {item.Value}"));
        var fileNameNote = string.Equals(Path.GetFileName(targetPath), Path.GetFileName(candidate.World.SavePath), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"\nIt will be saved as {Path.GetFileName(targetPath)}.";
        if (!_dialogService.Confirm("Import world", $"{summary}\n\n{verdict.Message}{fileNameNote}\n\n{ImportComparison.SafetyNote}"))
        {
            return;
        }

        await RunBusyAsync("Importing world...", async () =>
        {
            var snapshot = await _backupService.CreateImportSnapshotAsync(candidate);
            var importedPath = await _backupService.ImportBackupAsNewWorldAsync(snapshot.FilePath, profile, overwriteExisting: localExists);
            await FinishImportAsync(importedPath, candidate.World.WorldName);
        });
    }

    private async Task ImportFromBackupAsync(string backupPath)
    {
        BackupManifest manifest;
        GameSaveRoot? profile;
        try
        {
            manifest = await _backupService.ReadManifestAsync(backupPath, _activeGame.Current.Id);
            if (!SafePath.IsSafeSegment(manifest.WorldId))
            {
                throw new InvalidDataException("The backup contains an invalid world id.");
            }

            profile = await FindSaveRootAsync();
        }
        catch (Exception ex)
        {
            ReportImportError(ex);
            return;
        }

        if (profile is null)
        {
            return;
        }

        var targetWorldPath = _activeGame.Current.SaveAdapter.GetExpectedWorldPath(profile, manifest.WorldId);
        var worldExists = File.Exists(targetWorldPath) || Directory.Exists(targetWorldPath);
        var actionText = worldExists
            ? "This world already exists on this computer. Your copy is backed up first, then replaced by the backup."
            : "This adds the world to this computer.";

        var confirmed = _dialogService.Confirm(
            "Import world backup",
            $"World: {manifest.WorldName}\nBacked up: {manifest.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n\n{actionText}");
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Importing backup...", async () =>
        {
            var importedPath = await _backupService.ImportBackupAsNewWorldAsync(backupPath, profile, overwriteExisting: worldExists);
            await FinishImportAsync(importedPath, manifest.WorldName);
        });
    }

    private async Task FinishImportAsync(string importedPath, string worldName)
    {
        var importedWorld = await _activeGame.Current.SaveAdapter.ReadWorldAsync(importedPath);
        await RefreshAsync();
        if (importedWorld is not null)
        {
            SelectedWorld = Worlds.FirstOrDefault(world =>
                string.Equals(world.WorldId, importedWorld.WorldId, StringComparison.OrdinalIgnoreCase)) ?? SelectedWorld;
        }

        StatusText = $"Imported world: {worldName}";
        AddActivity("Success", StatusText);
        _toastService.Success("Import complete", worldName);
        _dialogService.ShowInfo("Import complete", $"Imported {worldName}.\n\nStart {ActiveGameName} and check that the world appears. If anything is wrong, the Backups tab can restore the previous copy.");
    }

    private async Task<GameSaveRoot?> FindSaveRootAsync()
    {
        var profile = (await _activeGame.Current.SaveAdapter.DiscoverSaveRootsAsync()).FirstOrDefault();
        if (profile is null)
        {
            _toastService.Warning("Profile not found", $"Start {ActiveGameName} once, close it, then import again.");
            _dialogService.ShowError(
                $"{ActiveGameName} profile not found",
                $"Start {ActiveGameName} once on this computer, let it reach the main menu or create its local profile, then close it and try importing again.");
            AddActivity("Error", $"Import blocked: {ActiveGameName} profile not found.");
        }

        return profile;
    }

    private void ReportImportError(Exception ex)
    {
        var error = _errorHandler.Handle(ex, "Read import file", AppLogKeyword.Import);
        _toastService.Error("Cannot import", error.UserMessage);
        _dialogService.ShowError("Cannot import", FormatDialogError(error));
        AddActivity("Error", FormatActivityError(error));
    }

    private bool IsBlockedByRunningGame(string action)
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning($"{ActiveGameName} is running", $"Close the game before {action}.");
            _dialogService.ShowError($"{ActiveGameName} is running", $"Close {ActiveGameName} before {action}, so the save files are not changed while the game uses them.");
        }

        return IsGameRunning;
    }
}
