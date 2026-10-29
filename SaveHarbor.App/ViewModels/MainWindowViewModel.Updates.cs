using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

// The update banner above the header, the version in the footer, "Check for updates" in Settings and the patch notes.
public partial class MainWindowViewModel
{
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(10);

    // "Later": the banner gives way to a dot on Settings, and the update installs when SaveHarbor closes.
    private bool updateBannerDismissed;

    public AppUpdateState UpdateState => _updates.State;

    public bool ShowUpdateBanner => !updateBannerDismissed && IsUpdateOffered;

    public bool HasUpdateDot => updateBannerDismissed && IsUpdateOffered;

    public bool IsUpdateReady => UpdateState == AppUpdateState.Ready;

    public bool IsUpdateDownloading => UpdateState == AppUpdateState.Downloading;

    public int UpdateProgress => _updates.DownloadProgress;

    public bool UpdatesUnavailable => !_updates.IsInstalled;

    public string UpdateBannerText => UpdateState switch
    {
        AppUpdateState.Available => $"SaveHarbor {_updates.Available?.Version} is available.",
        AppUpdateState.Downloading => $"Downloading update… {UpdateProgress} %",
        AppUpdateState.Ready => $"SaveHarbor {_updates.Available?.Version} is ready.",
        _ => string.Empty
    };

    public string AppVersionText
    {
        get
        {
            var current = AppEnvironment.IsDevelopment
                ? $"v{_updates.CurrentVersion.ToString(3)} · {AppEnvironment.Development}"
                : $"v{_updates.CurrentVersion.ToString(3)}";
            return UpdateState switch
            {
                AppUpdateState.Ready => $"{current} · {_updates.Available?.Version} ready",
                AppUpdateState.Available or AppUpdateState.Downloading => $"{current} · {_updates.Available?.Version} available",
                AppUpdateState.Idle when _updates.LastCheckedAtUtc is not null => $"{current} · up to date",
                _ => current
            };
        }
    }

    private bool IsUpdateOffered => UpdateState is AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.Ready;

    // Waits a little after start so the first screen is not slowed down; failures stay in the log.
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!_updates.IsInstalled)
        {
            return;
        }

        try
        {
            await Task.Delay(FirstUpdateCheckDelay);
            await _updates.CheckAsync(userInitiated: false);
        }
        catch (Exception ex)
        {
            _logger.Debug(AppLogKeyword.Updates, "Background update check failed: {Message}", ex.Message);
        }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (updateBannerDismissed && IsUpdateReady)
            {
                _updates.ApplyOnExit();
            }

            NotifyUpdateState();
        });

    private void NotifyUpdateState()
    {
        OnPropertyChanged(nameof(UpdateState));
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(HasUpdateDot));
        OnPropertyChanged(nameof(IsUpdateReady));
        OnPropertyChanged(nameof(IsUpdateDownloading));
        OnPropertyChanged(nameof(UpdateProgress));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(AppVersionText));
        RestartForUpdateCommand.NotifyCanExecuteChanged();
    }

    // Never while the game runs or this PC holds a session: the update then installs when SaveHarbor closes.
    [RelayCommand(CanExecute = nameof(IsUpdateReady))]
    private void RestartForUpdate()
    {
        UpdateGameStatus();
        if (IsGameRunning || HasOwnCloudSession())
        {
            _dialogService.ShowInfo("Finish playing first", "Finish playing first; the update installs when you close SaveHarbor.");
            DismissUpdateBanner();
            return;
        }

        if (_updates.ApplyAndRestart())
        {
            System.Windows.Application.Current?.Shutdown();
            return;
        }

        _dialogService.ShowError("Update not installed", "SaveHarbor could not start its updater. Your current version keeps working; see the log for details.");
    }

    [RelayCommand]
    private void Later() => DismissUpdateBanner();

    // Every version's notes from the embedded CHANGELOG.md, opened on the version that is running.
    [RelayCommand]
    private void OpenPatchNotes() =>
        _dialogService.ShowPatchNotes(new PatchNotesViewModel(PatchNotesCatalog.Releases, _updates.CurrentVersion.ToString(3)), _activeGame.Current.SkinDictionary);

    // Once per version, at the end of the start: the first start after an update opens the patch notes. While the game
    // runs nothing is recorded, so they open on the next start instead.
    private void OpenPatchNotesAfterUpdate()
    {
        var current = _updates.CurrentVersion.ToString(3);
        var settings = _settingsStore.Current;
        if (settings.PatchNotesShownVersion == current)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            return;
        }

        var usedBefore = settings.CloudProvider.Length > 0 || settings.LastGame.Length > 0 || settings.DeviceId.Length > 0 || settings.PlayerName.Length > 0;
        var open = PatchNotesViewModel.ShouldOpenAfterStart(settings.PatchNotesShownVersion, usedBefore, current)
            && PatchNotesCatalog.Releases.Any(release => release.Version == current);
        _settingsStore.Update(changed => changed.PatchNotesShownVersion = current);
        if (open)
        {
            _logger.Information(AppLogKeyword.Updates, "Opening the patch notes after the update to {Version}", current);
            OpenPatchNotes();
        }
    }

    [RelayCommand]
    private void ShowReleaseNotes()
    {
        if (_updates.Available is not { } update)
        {
            return;
        }

        var skip = _dialogService.ShowReleaseNotes($"What's new in SaveHarbor {update.Version}", update.NotesMarkdown, canSkip: !_updates.IsApplyOnExitArmed);
        if (skip)
        {
            _updates.SkipVersion(update.Version);
            updateBannerDismissed = false;
            AddActivity("Info", $"Update {update.Version} skipped. Check for updates in Settings to get it later.");
            NotifyUpdateState();
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (!_updates.IsInstalled)
        {
            _toastService.Info("Updates", "Updates are available in the installed version.");
            return;
        }

        updateBannerDismissed = false;
        await _updates.CheckAsync(userInitiated: true);
        NotifyUpdateState();
        switch (UpdateState)
        {
            case AppUpdateState.Idle:
                _toastService.Success("No update", $"You have the newest version ({_updates.CurrentVersion.ToString(3)}).");
                break;
            case AppUpdateState.Failed:
                _toastService.Warning("Update check failed", $"Could not check for updates: {_updates.LastError}");
                break;
        }
    }

    private void DismissUpdateBanner()
    {
        updateBannerDismissed = true;
        _updates.ApplyOnExit();
        NotifyUpdateState();
    }
}
