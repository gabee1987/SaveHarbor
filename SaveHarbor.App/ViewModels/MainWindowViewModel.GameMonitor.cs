using System.IO;
using SaveHarbor.App.Domain;

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
            return;
        }

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

    private async Task AutoEndCloudSessionAfterGameClosedAsync(GameWorld world)
    {
        isAutoEndingSession = true;
        try
        {
            StatusText = $"{ActiveGameName} closed. Ending cloud session...";
            AddActivity("Info", StatusText);

            var result = await _cloudSyncService.EndSessionAsync(world);
            await RefreshCloudStatusAsync(showToast: false);
            await RefreshCloudOverviewAsync();

            hasObservedGameRunningDuringSession = false;
            StatusText = result.Message;

            if (!result.IsSuccess)
            {
                AddActivity("Warning", result.Message);
                _toastService.Warning("Session not ended", result.Message);
                return;
            }

            AddActivity("Success", $"{ActiveGameName} closed. Session ended automatically.");
            _toastService.Success("Session ended", $"{ActiveGameName} closed, so SaveHarbor cleared your active session.");
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Auto end cloud session", AppLogKeyword.CloudSession);
            StatusText = "Could not auto-end session.";
            AddActivity("Error", FormatActivityError(error));
            _toastService.Warning("Session still active", error.UserMessage);
        }
        finally
        {
            isAutoEndingSession = false;
            UpdateGameStatus();
            NotifyCommandStates();
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
