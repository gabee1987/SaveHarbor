using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IWindroseSaveDiscoveryService _saveDiscoveryService;
    private readonly IBackupService _backupService;
    private readonly IProcessDetectionService _processDetectionService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly ICloudSyncService _cloudSyncService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenWorldFolderCommand))]
    private WindroseWorld? selectedWorld;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isGameRunning;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private BackupInfo? lastBackup;

    [ObservableProperty]
    private int backupCount;

    [ObservableProperty]
    private string totalBackupSize = "0 B";

    [ObservableProperty]
    private string profileStatus = "Checking Windrose profile...";

    [ObservableProperty]
    private CloudSyncStatus? cloudStatus;

    public ObservableCollection<WindroseWorld> Worlds { get; } = [];

    public ObservableCollection<ActivityLogItem> Activity { get; } = [];

    public ObservableCollection<ToastNotification> Toasts { get; } = [];

    public string BackupRoot => _backupService.BackupRoot;

    public string LocalSaveRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "R5",
        "Saved",
        "SaveProfiles");

    public string SelectedWorldSize => SelectedWorld is null ? "Unknown" : FormatBytes(SelectedWorld.SizeBytes);

    public string SelectedWorldFileCount => SelectedWorld is null ? "0 files" : $"{SelectedWorld.FileCount:N0} files";

    public string LatestBackupSummary => LastBackup is null
        ? "No backups found"
        : $"{LastBackup.FileName} • {FormatBytes(LastBackup.SizeBytes)}";

    public string LatestBackupFileName => LastBackup?.FileName ?? "No backups found";

    public string LatestBackupPath => LastBackup?.FilePath ?? "Create a backup to see the latest backup path here.";

    public string LatestBackupAge => LastBackup is null
        ? "Create a backup before sharing or restoring this world."
        : $"Created {FormatAge(LastBackup.CreatedAt)}";

    public string LatestBackupDetails => LastBackup is null
        ? "No backup has been created yet."
        : $"{FormatBytes(LastBackup.SizeBytes)} • {LatestBackupAge}";

    public string BackupStorageSummary => $"{BackupCount:N0} backups • {TotalBackupSize}";

    public string SelectedWorldModifiedAge => SelectedWorld is null
        ? "No world selected"
        : $"Modified {FormatAge(SelectedWorld.LastModifiedAt)}";

    public string SafetyHint => IsGameRunning
        ? "Close Windrose before backup, restore, upload, or download."
        : "Safe for backup and restore. Keep Windrose closed during save operations.";

    public string CloudStateText => CloudStatus?.Title ?? "Cloud not checked";

    public string CloudDetailText => CloudStatus?.Detail ?? "Check cloud status after selecting a world.";

    public string CloudProviderText => CloudStatus?.Connection.ProviderName ?? "Not configured";

    public string CloudAccountText => CloudStatus?.Connection.AccountEmail ?? "No account connected";

    public string CloudLatestVersionText => CloudStatus?.LatestVersion is null
        ? "No cloud version"
        : $"v{CloudStatus.LatestVersion.VersionNumber} by {CloudStatus.LatestVersion.UploadedBy}";

    public string CloudLocalBaseText => CloudStatus?.LocalState.LocalBaseVersionNumber is null
        ? "No local base version"
        : $"Local base v{CloudStatus.LocalState.LocalBaseVersionNumber}";

    public string CloudSessionText => CloudStatus?.SessionLock is null
        ? "No active session"
        : $"{CloudStatus.SessionLock.PlayerName} started from v{CloudStatus.SessionLock.BasedOnVersionNumber}";

    public MainWindowViewModel(
        IWindroseSaveDiscoveryService saveDiscoveryService,
        IBackupService backupService,
        IProcessDetectionService processDetectionService,
        IDialogService dialogService,
        IToastService toastService,
        ICloudSyncService cloudSyncService)
    {
        _saveDiscoveryService = saveDiscoveryService;
        _backupService = backupService;
        _processDetectionService = processDetectionService;
        _dialogService = dialogService;
        _toastService = toastService;
        _cloudSyncService = cloudSyncService;

        _toastService.ToastRequested += OnToastRequested;
    }

    partial void OnSelectedWorldChanged(WindroseWorld? value)
    {
        OnPropertyChanged(nameof(SelectedWorldSize));
        OnPropertyChanged(nameof(SelectedWorldFileCount));
        OnPropertyChanged(nameof(SelectedWorldModifiedAge));
        _ = RefreshCloudStatusAsync(showToast: false);
    }

    partial void OnCloudStatusChanged(CloudSyncStatus? value)
    {
        OnPropertyChanged(nameof(CloudStateText));
        OnPropertyChanged(nameof(CloudDetailText));
        OnPropertyChanged(nameof(CloudProviderText));
        OnPropertyChanged(nameof(CloudAccountText));
        OnPropertyChanged(nameof(CloudLatestVersionText));
        OnPropertyChanged(nameof(CloudLocalBaseText));
        OnPropertyChanged(nameof(CloudSessionText));
    }

    partial void OnLastBackupChanged(BackupInfo? value)
    {
        OnPropertyChanged(nameof(LatestBackupSummary));
        OnPropertyChanged(nameof(LatestBackupFileName));
        OnPropertyChanged(nameof(LatestBackupPath));
        OnPropertyChanged(nameof(LatestBackupAge));
        OnPropertyChanged(nameof(LatestBackupDetails));
    }

    partial void OnBackupCountChanged(int value)
    {
        OnPropertyChanged(nameof(BackupStorageSummary));
    }

    partial void OnTotalBackupSizeChanged(string value)
    {
        OnPropertyChanged(nameof(BackupStorageSummary));
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SafetyHint));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("Scanning for Windrose worlds...", async () =>
        {
            UpdateGameStatus();
            Worlds.Clear();

            var worlds = await _saveDiscoveryService.DiscoverWorldsAsync();
            foreach (var world in worlds)
            {
                Worlds.Add(world);
            }

            SelectedWorld ??= Worlds.FirstOrDefault();
            await RefreshProfileStatusAsync();
            await RefreshBackupStatsAsync();
            await RefreshCloudStatusAsync(showToast: false);
            StatusText = Worlds.Count == 0
                ? "No Windrose worlds found."
                : $"Found {Worlds.Count} world{(Worlds.Count == 1 ? string.Empty : "s")}.";

            AddActivity("Info", StatusText);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task CreateBackupAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before creating a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before creating a backup so the RocksDB save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Creating backup...", async () =>
        {
            LastBackup = await _backupService.CreateBackupAsync(SelectedWorld, "manual");
            await RefreshBackupStatsAsync();
            StatusText = $"Backup created: {LastBackup.FileName}";
            AddActivity("Success", StatusText);
            _toastService.Success("Backup created", LastBackup.FileName);
            _dialogService.ShowInfo("Backup created", $"Saved backup:\n{LastBackup.FilePath}");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private async Task RestoreBackupAsync()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before restoring a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before restoring a backup.");
            return;
        }

        var backupPath = _dialogService.SelectZipFile(_backupService.BackupRoot);
        if (backupPath is null)
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Restore backup",
            $"SaveHarbor will create a safety backup first, then replace this world:\n\n{SelectedWorld.WorldName}\n\nWith:\n{Path.GetFileName(backupPath)}");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Restoring backup...", async () =>
        {
            await _backupService.RestoreBackupAsync(backupPath, SelectedWorld);
            var refreshed = await _saveDiscoveryService.ReadWorldAsync(SelectedWorld.SavePath);
            if (refreshed is not null)
            {
                var index = Worlds.IndexOf(SelectedWorld);
                if (index >= 0)
                {
                    Worlds[index] = refreshed;
                }

                SelectedWorld = refreshed;
            }

            StatusText = "Backup restored successfully.";
            await RefreshBackupStatsAsync();
            AddActivity("Success", StatusText);
            _toastService.Success("Restore complete", "Backup restored and safety backup created.");
            _dialogService.ShowInfo("Restore complete", "The backup was restored and a pre-restore safety backup was created.");
        });
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning("Windrose is running", "Close the game before importing a backup.");
            _dialogService.ShowError("Windrose is running", "Close Windrose before importing a world backup.");
            return;
        }

        var backupPath = _dialogService.SelectZipFile(_backupService.BackupRoot);
        if (backupPath is null)
        {
            return;
        }

        BackupManifest manifest;
        IReadOnlyList<WindroseProfile> profiles;

        try
        {
            manifest = await _backupService.ReadManifestAsync(backupPath);
            profiles = await _saveDiscoveryService.DiscoverProfilesAsync();
        }
        catch (Exception ex)
        {
            _toastService.Error("Cannot import backup", ex.Message);
            _dialogService.ShowError("Cannot import backup", ex.Message);
            AddActivity("Error", ex.Message);
            return;
        }

        var profile = profiles.FirstOrDefault();
        if (profile is null)
        {
            _toastService.Warning("Profile not found", "Start Windrose once, close it, then import again.");
            _dialogService.ShowError(
                "Windrose profile not found",
                "Start Windrose once on this computer, let it reach the main menu or create its local profile, then close it and try importing again.");
            AddActivity("Error", "Import blocked: Windrose profile not found.");
            return;
        }

        var targetWorldPath = Path.Combine(profile.WorldsPath, manifest.WorldId);
        var worldExists = Directory.Exists(targetWorldPath);
        var actionText = worldExists
            ? "This world already exists on this computer. Importing will replace that local world folder."
            : "This will add the world to this computer.";

        var confirmed = _dialogService.Confirm(
            "Import world backup",
            $"World: {manifest.WorldName}\nWorld ID: {manifest.WorldId}\nProfile: {profile.ProfileId}\n\n{actionText}\n\nKeep Windrose closed while importing.");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Importing backup as a new world...", async () =>
        {
            var importedPath = await _backupService.ImportBackupAsNewWorldAsync(backupPath, profile, overwriteExisting: worldExists);
            await RefreshBackupStatsAsync();
            await RefreshProfileStatusAsync();

            var importedWorld = await _saveDiscoveryService.ReadWorldAsync(importedPath);
            await RefreshAsync();
            if (importedWorld is not null)
            {
                SelectedWorld = Worlds.FirstOrDefault(world =>
                    string.Equals(world.WorldId, importedWorld.WorldId, StringComparison.OrdinalIgnoreCase));
            }

            StatusText = $"Imported world backup: {manifest.WorldName}";
            AddActivity("Success", StatusText);
            _toastService.Success("Import complete", manifest.WorldName);
            _dialogService.ShowInfo("Import complete", $"Imported {manifest.WorldName}.\n\nStart Windrose and check that the world appears.");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private void OpenWorldFolder()
    {
        if (SelectedWorld is not null)
        {
            OpenFolder(SelectedWorld.SavePath);
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(_backupService.BackupRoot);
        OpenFolder(_backupService.BackupRoot);
    }

    [RelayCommand]
    private async Task CheckCloudAsync()
    {
        await RefreshCloudStatusAsync(showToast: true);
    }

    private bool HasSelectedWorld()
    {
        return SelectedWorld is not null && !IsBusy;
    }

    private async Task RunBusyAsync(string busyText, Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusText = busyText;
            NotifyCommandStates();
            await action();
        }
        catch (Exception ex)
        {
            StatusText = "Operation failed.";
            AddActivity("Error", ex.Message);
            _toastService.Error("Operation failed", ex.Message);
            _dialogService.ShowError("SaveHarbor error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            UpdateGameStatus();
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        CreateBackupCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
        OpenWorldFolderCommand.NotifyCanExecuteChanged();
    }

    private void UpdateGameStatus()
    {
        IsGameRunning = _processDetectionService.IsWindroseRunning();
    }

    private async Task RefreshBackupStatsAsync()
    {
        var backups = await _backupService.ListBackupsAsync();
        BackupCount = backups.Count;
        TotalBackupSize = FormatBytes(backups.Sum(backup => backup.SizeBytes));
        LastBackup = backups.FirstOrDefault();
    }

    private async Task RefreshProfileStatusAsync()
    {
        var profiles = await _saveDiscoveryService.DiscoverProfilesAsync();
        var profile = profiles.FirstOrDefault();

        ProfileStatus = profile is null
            ? "No Windrose profile found. Start Windrose once before importing a backup."
            : $"Profile {profile.ProfileId} • RocksDB {profile.RocksDbVersion}";
    }

    private async Task RefreshCloudStatusAsync(bool showToast)
    {
        if (SelectedWorld is null)
        {
            CloudStatus = null;
            return;
        }

        try
        {
            CloudStatus = await _cloudSyncService.RefreshStatusAsync(SelectedWorld);
            if (showToast)
            {
                _toastService.Info(CloudStatus.Title, CloudStatus.Detail);
                AddActivity("Info", $"{CloudStatus.Title}: {CloudStatus.Detail}");
            }
        }
        catch (Exception ex)
        {
            AddActivity("Error", ex.Message);
            _toastService.Error("Cloud check failed", ex.Message);
        }
    }

    private void AddActivity(string level, string message)
    {
        Activity.Insert(0, new ActivityLogItem(DateTimeOffset.Now, level, message));
        while (Activity.Count > 12)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private async void OnToastRequested(object? sender, ToastNotification toast)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Toasts.Insert(0, toast);
            while (Toasts.Count > 4)
            {
                Toasts.RemoveAt(Toasts.Count - 1);
            }
        });

        await Task.Delay(TimeSpan.FromSeconds(toast.Kind == ToastKind.Error ? 6 : 3.5));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            toast.IsClosing = true;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(460));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Toasts.Remove(toast);
        });
    }

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatAge(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp;
        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{(int)elapsed.TotalHours} h ago";
        }

        if (elapsed.TotalDays < 14)
        {
            return $"{(int)elapsed.TotalDays} d ago";
        }

        return timestamp.ToString("yyyy-MM-dd HH:mm");
    }
}
