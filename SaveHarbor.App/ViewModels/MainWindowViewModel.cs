using System.Collections.ObjectModel;
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
    [NotifyCanExecuteChangedFor(nameof(UploadCloudCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCloudCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCloudSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndCloudSessionCommand))]
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

    public ObservableCollection<WindroseWorld> Worlds { get; } = [];

    public ObservableCollection<ActivityLogItem> Activity { get; } = [];

    public ObservableCollection<ToastNotification> Toasts { get; } = [];
}
