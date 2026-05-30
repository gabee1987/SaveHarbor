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
            }

            return;
        }

        if (!wasRunning || !hasObservedGameRunningDuringSession || SelectedWorld is null || !HasOwnCloudSession())
        {
            return;
        }

        await AutoEndCloudSessionAfterGameClosedAsync(SelectedWorld);
    }

    private async Task AutoEndCloudSessionAfterGameClosedAsync(WindroseWorld world)
    {
        isAutoEndingSession = true;
        try
        {
            StatusText = "Windrose closed. Ending cloud session...";
            AddActivity("Info", StatusText);

            var result = await _cloudSyncService.EndSessionAsync(world);
            await RefreshCloudStatusAsync(showToast: false);

            hasObservedGameRunningDuringSession = false;
            StatusText = result.Message;

            if (!result.IsSuccess)
            {
                AddActivity("Warning", result.Message);
                _toastService.Warning("Session not ended", result.Message);
                return;
            }

            AddActivity("Success", "Windrose closed. Session ended automatically.");
            _toastService.Success("Session ended", "Windrose closed, so SaveHarbor cleared your active session.");
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

    private bool HasOwnCloudSession()
    {
        return CloudStatus?.SessionLock is not null &&
            string.Equals(CloudStatus.SessionLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
