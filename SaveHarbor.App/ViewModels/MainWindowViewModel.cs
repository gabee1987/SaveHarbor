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

    public ObservableCollection<WindroseWorld> Worlds { get; } = [];

    public ObservableCollection<ActivityLogItem> Activity { get; } = [];

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

    public string LatestBackupAge => LastBackup is null
        ? "Create a backup before sharing or restoring this world."
        : $"Created {FormatAge(LastBackup.CreatedAt)}";

    public string SelectedWorldModifiedAge => SelectedWorld is null
        ? "No world selected"
        : $"Modified {FormatAge(SelectedWorld.LastModifiedAt)}";

    public string SafetyHint => IsGameRunning
        ? "Close Windrose before backup, restore, upload, or download."
        : "Safe for backup and restore. Keep Windrose closed during save operations.";

    public MainWindowViewModel(
        IWindroseSaveDiscoveryService saveDiscoveryService,
        IBackupService backupService,
        IProcessDetectionService processDetectionService,
        IDialogService dialogService)
    {
        _saveDiscoveryService = saveDiscoveryService;
        _backupService = backupService;
        _processDetectionService = processDetectionService;
        _dialogService = dialogService;
    }

    partial void OnSelectedWorldChanged(WindroseWorld? value)
    {
        OnPropertyChanged(nameof(SelectedWorldSize));
        OnPropertyChanged(nameof(SelectedWorldFileCount));
        OnPropertyChanged(nameof(SelectedWorldModifiedAge));
    }

    partial void OnLastBackupChanged(BackupInfo? value)
    {
        OnPropertyChanged(nameof(LatestBackupSummary));
        OnPropertyChanged(nameof(LatestBackupAge));
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
            await RefreshBackupStatsAsync();
            StatusText = Worlds.Count == 0
                ? "No Windrose worlds found."
                : $"Found {Worlds.Count} Windrose world{(Worlds.Count == 1 ? string.Empty : "s")}.";

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
            _dialogService.ShowError("Windrose is running", "Close Windrose before creating a backup so the RocksDB save files are not copied while they are changing.");
            return;
        }

        await RunBusyAsync("Creating backup...", async () =>
        {
            LastBackup = await _backupService.CreateBackupAsync(SelectedWorld, "manual");
            await RefreshBackupStatsAsync();
            StatusText = $"Backup created: {LastBackup.FileName}";
            AddActivity("Success", StatusText);
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
            _dialogService.ShowInfo("Restore complete", "The backup was restored and a pre-restore safety backup was created.");
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
    private void CheckCloud()
    {
        _dialogService.ShowInfo("Cloud sync", "Cloud sync is planned after local backup and restore are verified. The UI is ready for it, but the provider is not implemented yet.");
        AddActivity("Info", "Cloud sync is not implemented yet.");
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

    private void AddActivity(string level, string message)
    {
        Activity.Insert(0, new ActivityLogItem(DateTimeOffset.Now, level, message));
        while (Activity.Count > 12)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
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
