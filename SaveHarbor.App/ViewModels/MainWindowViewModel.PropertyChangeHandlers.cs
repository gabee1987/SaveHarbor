using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedWorldChanged(WindroseWorld? value)
    {
        OnPropertyChanged(nameof(SelectedWorldSize));
        OnPropertyChanged(nameof(SelectedWorldFileCount));
        OnPropertyChanged(nameof(SelectedWorldModifiedAge));

        if (!suppressSelectedWorldCloudRefresh)
        {
            _ = RefreshCloudStatusAsync(showToast: false);
        }
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
        OnPropertyChanged(nameof(CloudSessionTooltip));
    }

    partial void OnLastBackupChanged(BackupInfo? value)
    {
        OnPropertyChanged(nameof(LatestBackupSummary));
        OnPropertyChanged(nameof(LatestBackupFileName));
        OnPropertyChanged(nameof(LatestBackupPath));
        OnPropertyChanged(nameof(LatestBackupAge));
        OnPropertyChanged(nameof(LatestBackupDetails));
        OnPropertyChanged(nameof(LatestBackupHeader));
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
}
