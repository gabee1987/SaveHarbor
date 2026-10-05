using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool isSettingsOpen;

    public SettingsViewModel? Settings { get; private set; }

    public bool AmbientEffectsEnabled => _settingsStore.Current.AmbientEffects;

    public string PlayerDisplayName => _playerIdentity.DisplayName;

    [RelayCommand]
    private void OpenSettings()
    {
        if (Settings is null)
        {
            Settings = new SettingsViewModel(
                _settingsStore,
                _activeGame,
                _dialogService,
                _playerIdentity,
                _paths,
                saveFolderChanged: () => RefreshCommand.Execute(null),
                settingsChanged: () =>
                {
                    OnPropertyChanged(nameof(AmbientEffectsEnabled));
                    OnPropertyChanged(nameof(PlayerDisplayName));
                });
            OnPropertyChanged(nameof(Settings));
        }

        Settings.Refresh();
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }
}
