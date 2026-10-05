using CommunityToolkit.Mvvm.Input;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
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
            await RunBusyAsync($"Starting session for running {ActiveGameName}...", async () =>
            {
                var sessionStarted = await TryStartCloudSessionAsync(SelectedWorld);
                if (!sessionStarted)
                {
                    return;
                }

                hasObservedGameRunningDuringSession = true;
                StatusText = $"{ActiveGameName} is running. Session is active.";
                AddActivity("Info", StatusText);
                _toastService.Success("Session active", $"{ActiveGameName} is already running, so SaveHarbor will end the session when the game closes.");
            });
            return;
        }

        await RunBusyAsync($"Starting session and launching {ActiveGameName}...", async () =>
        {
            var sessionStarted = await TryStartCloudSessionAsync(SelectedWorld);
            if (!sessionStarted)
            {
                return;
            }

            var launchResult = await _gameLauncherService.LaunchAsync(_activeGame.Current);
            StatusText = launchResult.Message;

            if (!launchResult.IsSuccess)
            {
                AddActivity("Warning", launchResult.Message);
                _toastService.Warning("Launch failed", launchResult.Message);
                _dialogService.ShowError("Launch failed", launchResult.Message);
                return;
            }

            AddActivity("Success", $"Session active. {ActiveGameName} launch requested.");
            _toastService.Success($"Starting {ActiveGameName}", $"Session is active and Steam has been asked to launch {ActiveGameName}.");
        });
    }

    // Joining a friend's hosted game needs no session, lock or download: the world lives on the host's PC.
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task JoinGameAsync()
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Info($"{ActiveGameName} is already running", "Join your host from the game's online menu.");
            return;
        }

        await RunBusyAsync($"Launching {ActiveGameName} to join a friend...", async () =>
        {
            var launchResult = await _gameLauncherService.LaunchAsync(_activeGame.Current);
            StatusText = launchResult.Message;

            if (!launchResult.IsSuccess)
            {
                AddActivity("Warning", launchResult.Message);
                _toastService.Warning("Launch failed", launchResult.Message);
                _dialogService.ShowError("Launch failed", launchResult.Message);
                return;
            }

            AddActivity("Info", $"{ActiveGameName} launched to join a friend. No world was synced or locked.");
            _toastService.Success($"Starting {ActiveGameName}", "Join your host from the game's online menu.");
        });
    }
}
