using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool isSettingsOpen;

    public SettingsViewModel? Settings { get; private set; }

    public bool AmbientEffectsEnabled => _settingsStore.Current.AmbientEffects;

    public string PlayerDisplayName => _playerIdentity.DisplayName;

    [RelayCommand]
    private Task OpenSettingsAsync() => ShowSettingsAsync(sharingPage: false);

    private async Task ShowSettingsAsync(bool sharingPage)
    {
        if (Settings is null)
        {
            Settings = new SettingsViewModel(
                _settingsStore,
                _activeGame,
                _dialogService,
                _paths,
                saveFolderChanged: () => RefreshCommand.Execute(null),
                settingsChanged: () => OnPropertyChanged(nameof(AmbientEffectsEnabled)));
            OnPropertyChanged(nameof(Settings));
        }

        if (sharingPage)
        {
            Settings.SelectedPage = SettingsViewModel.SharingPage;
        }

        Settings.Refresh();
        if (IsSettingsOpen)
        {
            return;
        }

        var sharing = CreateSharing();
        Settings.Sharing = sharing;
        IsSettingsOpen = true;

        try
        {
            await sharing.InitializeAsync();
        }
        catch (Exception ex)
        {
            _errorHandler.Handle(ex, "Open Sharing", AppLogKeyword.Ui);
        }
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    // Also closed by a game switch, so the cloud is read again here and not in the command.
    partial void OnIsSettingsOpenChanged(bool value)
    {
        if (!value && Settings?.Sharing is { } sharing)
        {
            Settings.Sharing = null;
            _ = AfterSharingAsync(sharing);
        }
    }
}
