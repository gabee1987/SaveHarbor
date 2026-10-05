using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public sealed record RetentionOption(string Label, int Count)
{
    public override string ToString() => Label;
}

// Created the first time Settings is opened. Every change is applied and saved immediately.
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settings;
    private readonly IActiveGameContext _activeGame;
    private readonly IDialogService _dialogService;
    private readonly IPlayerIdentity _playerIdentity;
    private readonly IAppDataPathProvider _paths;
    private readonly Action _saveFolderChanged;
    private readonly Action _settingsChanged;

    public SettingsViewModel(
        IAppSettingsStore settings,
        IActiveGameContext activeGame,
        IDialogService dialogService,
        IPlayerIdentity playerIdentity,
        IAppDataPathProvider paths,
        Action saveFolderChanged,
        Action settingsChanged)
    {
        _settings = settings;
        _activeGame = activeGame;
        _dialogService = dialogService;
        _playerIdentity = playerIdentity;
        _paths = paths;
        _saveFolderChanged = saveFolderChanged;
        _settingsChanged = settingsChanged;
        Refresh();
    }

    public IReadOnlyList<RetentionOption> RetentionOptions { get; } =
    [
        new("Keep every backup", 0),
        new("Keep the newest 10", 10),
        new("Keep the newest 25", 25),
        new("Keep the newest 50", 50)
    ];

    public string GameName => _activeGame.Current.DisplayName;

    public string PlayerName
    {
        get => _settings.Current.PlayerName;
        set
        {
            var normalized = PlayerIdentity.Normalize(value);
            if (normalized == _settings.Current.PlayerName)
            {
                return;
            }

            _settings.Update(settings => settings.PlayerName = normalized);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectivePlayerName));
            _settingsChanged();
        }
    }

    public string EffectivePlayerName => _playerIdentity.DisplayName;

    public RetentionOption SelectedRetention
    {
        get => RetentionOptions.FirstOrDefault(option => option.Count == _settings.Current.BackupRetentionCount) ?? RetentionOptions[0];
        set
        {
            if (value is null || value.Count == _settings.Current.BackupRetentionCount)
            {
                return;
            }

            _settings.Update(settings => settings.BackupRetentionCount = value.Count);
            OnPropertyChanged();
        }
    }

    public bool AmbientEffects
    {
        get => _settings.Current.AmbientEffects;
        set
        {
            if (value == _settings.Current.AmbientEffects)
            {
                return;
            }

            _settings.Update(settings => settings.AmbientEffects = value);
            OnPropertyChanged();
            _settingsChanged();
        }
    }

    public string SaveFolder => _activeGame.Current.SaveAdapter.SaveRootPath;

    public string BackupFolder => _paths.GetBackupRoot(_activeGame.Current.Id);

    public bool HasCustomSaveFolder => _settings.Current.SaveRootOverrides.ContainsKey(_activeGame.Current.StorageKey);

    public void Refresh()
    {
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(SaveFolder));
        OnPropertyChanged(nameof(BackupFolder));
        OnPropertyChanged(nameof(HasCustomSaveFolder));
        OnPropertyChanged(nameof(EffectivePlayerName));
    }

    [RelayCommand]
    private void BrowseSaveFolder()
    {
        var folder = _dialogService.SelectFolder($"Choose the {GameName} save folder", SaveFolder);
        if (folder is null)
        {
            return;
        }

        _settings.Update(settings => settings.SaveRootOverrides[_activeGame.Current.StorageKey] = folder);
        Refresh();
        _saveFolderChanged();
    }

    [RelayCommand]
    private void ResetSaveFolder()
    {
        if (!HasCustomSaveFolder)
        {
            return;
        }

        _settings.Update(settings => settings.SaveRootOverrides.Remove(_activeGame.Current.StorageKey));
        Refresh();
        _saveFolderChanged();
    }

    [RelayCommand]
    private void OpenSaveFolder() => ExplorerLauncher.OpenExistingFolder(SaveFolder);

    [RelayCommand]
    private void OpenBackupFolder() => ExplorerLauncher.OpenFolder(BackupFolder);

    [RelayCommand]
    private void OpenDataFolder() => ExplorerLauncher.OpenFolder(_paths.AppDataRoot);

    [RelayCommand]
    private void OpenLogsFolder() => ExplorerLauncher.OpenFolder(_paths.LocalLogsPath);
}
