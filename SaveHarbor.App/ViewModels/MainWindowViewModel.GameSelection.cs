using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    public string ActiveGameName => _activeGame.Current.DisplayName;

    public IReadOnlyList<IGameDefinition> AvailableGames => _gameRegistry.All;

    public bool CanSwitchGame => !IsBusy && !IsGameRunning && !HasOwnCloudSession();

    public string SwitchGameDisabledReason => IsBusy
        ? "Wait for the current operation to finish."
        : IsGameRunning
            ? $"Close {ActiveGameName} before switching games."
            : HasOwnCloudSession()
                ? $"End your {ActiveGameName} session before switching games."
                : string.Empty;

    [RelayCommand(CanExecute = nameof(CanSwitchGame))]
    private void SwitchGame(GameId game)
    {
        _activeGame.SetActive(game);
    }

    private async void OnActiveGameChanged(object? sender, IGameDefinition game)
    {
        try
        {
            Worlds.Clear();
            SelectedWorld = null;
            CloudStatus = null;
            hasObservedGameRunningDuringSession = false;

            OnPropertyChanged(nameof(ActiveGameName));
            OnPropertyChanged(nameof(SafetyHint));
            OnPropertyChanged(nameof(LocalSaveRoot));
            OnPropertyChanged(nameof(SwitchGameDisabledReason));

            AddActivity("Info", $"Switched to {game.DisplayName}.");
            await RefreshAsync();
            await PromptCloudFolderSetupIfNeededAsync();
        }
        catch (Exception ex)
        {
            _errorHandler.Handle(ex, "Switch game", AppLogKeyword.Ui);
        }
    }

    private void NotifySwitchGameState()
    {
        OnPropertyChanged(nameof(CanSwitchGame));
        OnPropertyChanged(nameof(SwitchGameDisabledReason));
        SwitchGameCommand.NotifyCanExecuteChanged();
    }
}
