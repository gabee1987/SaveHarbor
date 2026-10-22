using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private void StartGameMonitor()
    {
        if (!gameMonitorTimer.IsEnabled)
        {
            gameMonitorTimer.Start();
        }
    }

    private async void OnGameMonitorTick(object? sender, EventArgs e)
    {
        if (isAutoEndingSession || IsBusy)
        {
            return;
        }

        NotifyRelativeTimes();
        var wasRunning = IsGameRunning;
        UpdateGameStatus();

        if (IsGameRunning)
        {
            if (HasOwnCloudSession())
            {
                hasObservedGameRunningDuringSession = true;
                await HeartbeatCloudSessionAsync();
            }

            return;
        }

        // The game saved while it ran: show the world's new save time and size.
        if (wasRunning)
        {
            try
            {
                await RefreshSelectedWorldFromDiskAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(AppLogKeyword.Ui, "Could not re-read the world after the game closed: {Message}", ex.Message);
            }
        }

        if (wasRunning && hasObservedGameRunningDuringSession && SelectedWorld is not null && HasOwnCloudSession())
        {
            await AutoEndCloudSessionAfterGameClosedAsync(SelectedWorld);
            RestoreFromTray();
            return;
        }

        if (wasRunning)
        {
            RestoreFromTray();
        }

        RestoreFromTrayIfGameNeverStarted();

        await RefreshIfWorldsChangedAsync();
    }

    // Worlds added or deleted outside SaveHarbor, typically in the game's own menu, appear without a manual refresh.
    // Only checked while the game is closed, so a rescan never reads a world the game is writing.
    private async Task RefreshIfWorldsChangedAsync()
    {
        var fingerprint = await ReadWorldFolderFingerprintAsync();
        if (fingerprint is null || knownWorldFolderFingerprint is null || fingerprint == knownWorldFolderFingerprint || IsBusy)
        {
            knownWorldFolderFingerprint ??= fingerprint;
            return;
        }

        AddActivity("Info", $"{ActiveGameName}'s save folder changed, so SaveHarbor scanned it again.");
        await RefreshAsync();
    }

    // The names directly inside each world folder of the game: they change when a world is added or deleted. Only
    // names are read, never file contents. Null when the folder cannot be read right now.
    private async Task<string?> ReadWorldFolderFingerprintAsync()
    {
        try
        {
            var roots = await _activeGame.Current.SaveAdapter.DiscoverSaveRootsAsync();
            return string.Join('|', roots
                .Select(root => root.WorldsPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => path + ">" + (Directory.Exists(path)
                    ? string.Join(',', Directory.EnumerateFileSystemEntries(path).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase))
                    : string.Empty)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The game closed on this PC's session: wait until the save is finished, share it, then release the lock. When
    // sharing fails the lock is released anyway, so friends are not blocked, and the banner keeps the progress in view.
    private async Task AutoEndCloudSessionAfterGameClosedAsync(GameWorld world)
    {
        isAutoEndingSession = true;
        IsBusy = true;
        NotifyCommandStates();
        try
        {
            StatusText = $"{ActiveGameName} closed. Waiting for the save to finish...";
            AddActivity("Info", StatusText);
            var adapter = _activeGame.Current.SaveAdapter;
            await SaveSettleWaiter.WaitAsync(adapter, world, SaveSettleWaiter.StableFor, SaveSettleWaiter.MaxWait);
            if (SelectedWorld is { } selected && IsSameWorld(world, selected))
            {
                await RefreshSelectedWorldFromDiskAsync();
            }

            var current = await adapter.ReadWorldAsync(world.SavePath) ?? world;
            var share = _settingsStore.Current.UploadWhenGameCloses || AskToShareAfterGameClosed(current);
            StatusText = share ? $"Sharing {current.WorldName}..." : $"Ending the session on {current.WorldName}...";
            var upload = share ? await ShareAfterGameClosedAsync(current) : null;
            var end = await _cloudSyncService.EndSessionAsync(current);
            hasObservedGameRunningDuringSession = false;
            await RefreshBackupStatsAsync();
            await RefreshCloudAsync("game closed");
            await RefreshPendingUploadAsync();

            if (upload is null)
            {
                // The session marked the world as not shared yet, so the banner keeps the progress in view.
                StatusText = $"{current.WorldName} is free again; your progress stays on this PC until you upload it.";
                AddActivity("Info", $"{ActiveGameName} closed. {StatusText}");
                _toastService.Info("Progress not shared", "Upload it from the banner when you are ready.");
            }
            else if (upload.IsSuccess)
            {
                AddActivity("Success", $"{ActiveGameName} closed. {upload.Message}");
                _toastService.Success("Progress shared", upload.Message);
                NotifyTray("Progress shared", upload.Message);
            }
            else
            {
                AddActivity("Warning", $"{ActiveGameName} closed, but the progress was not shared: {upload.Message}");
                _toastService.Warning("Progress not shared yet", upload.Message);
                NotifyTray("Progress not shared yet", upload.Message);
            }

            if (!end.IsSuccess)
            {
                AddActivity("Warning", end.Message);
                _toastService.Warning("Session not ended", end.Message);
            }
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Share after the game closed", AppLogKeyword.CloudSession);
            StatusText = "Could not share the progress or end the session.";
            AddActivity("Error", FormatActivityError(error));
            _toastService.Warning("Session still active", error.UserMessage);
            await RefreshPendingUploadAsync();
        }
        finally
        {
            isAutoEndingSession = false;
            IsBusy = false;
            UpdateGameStatus();
            NotifyCommandStates();
        }
    }

    // "Share progress when the game closes" is off: the window comes back and asks. Not now still frees the world, so
    // friends are not blocked, and says what that means for them.
    private bool AskToShareAfterGameClosed(GameWorld world)
    {
        RestoreFromTray();
        return _dialogService.Confirm(
            "Share your progress?",
            $"{ActiveGameName} closed. Share your progress in {world.WorldName} with your group now?\n\n"
            + "If you choose not now, the world is freed anyway: friends who play it before you upload start from the version before your session. "
            + "A reminder stays until you upload.");
    }

    // A copy the game would not load as it is, or one it swapped for another, is not shared without the player.
    private async Task<CloudSyncResult> ShareAfterGameClosedAsync(GameWorld world)
    {
        if (_activeGame.Current.SaveAdapter.CheckHealth(world)?.FirstOrDefault(notice => notice.Issue is not SaveHealthIssue.Leftovers) is { } problem)
        {
            return new CloudSyncResult(false, CloudSyncState.Error, $"{problem.Title}. Check the world, then share it with Retry upload.");
        }

        try
        {
            return await _cloudSyncService.UploadCurrentAsync(world);
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Upload after the game closed", AppLogKeyword.CloudUpload);
            return new CloudSyncResult(false, CloudSyncState.Error, error.UserMessage);
        }
    }

    // Keeps the session lock alive while the game runs. The service reaches the cloud only once per heartbeat interval,
    // so this runs on every tick.
    private async Task HeartbeatCloudSessionAsync()
    {
        if (isHeartbeating || SelectedWorld is not { } world)
        {
            return;
        }

        isHeartbeating = true;
        try
        {
            var result = await _cloudSyncService.HeartbeatSessionAsync(world);
            if (result.State == CloudSyncState.Conflict)
            {
                StatusText = result.Message;
                AddActivity("Warning", result.Message);
                _toastService.Warning("Session lock lost", result.Message);
                await RefreshCloudStatusAsync(showToast: false);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(AppLogKeyword.CloudSession, "Session heartbeat failed: {Message}", ex.Message);
        }
        finally
        {
            isHeartbeating = false;
        }
    }

    private bool HasOwnCloudSession()
    {
        return CloudStatus?.SessionLock is not null && CloudStatus.IsOwnSession;
    }
}
