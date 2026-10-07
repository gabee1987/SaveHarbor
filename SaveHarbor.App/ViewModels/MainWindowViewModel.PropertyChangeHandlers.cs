using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedWorldChanged(GameWorld? value)
    {
        OnPropertyChanged(nameof(SelectedWorldSize));
        RefreshSelectedWorldBackups();
        NotifySyncComparison();
        _ = RefreshWorldFactsAsync();

        if (!suppressSelectedWorldCloudRefresh)
        {
            _ = RefreshCloudStatusAsync(showToast: false);
        }
    }

    partial void OnCloudStatusChanged(CloudSyncStatus? value)
    {
        NotifySwitchGameState();
        OnPropertyChanged(nameof(CloudStateText));
        OnPropertyChanged(nameof(CloudDetailText));
        OnPropertyChanged(nameof(CloudProviderText));
        OnPropertyChanged(nameof(CloudAccountText));
        OnPropertyChanged(nameof(CloudLatestVersionText));
        OnPropertyChanged(nameof(CloudSessionText));
        OnPropertyChanged(nameof(CloudSessionTooltip));
        OnPropertyChanged(nameof(IsCloudConnected));
        OnPropertyChanged(nameof(HasSessionLock));
        OnPropertyChanged(nameof(IsOwnSession));
        OnPropertyChanged(nameof(IsOtherPlayerHosting));
        NotifySyncComparison();
        ApplySelectedStatusToBadges(value);
    }

    partial void OnBackupCountChanged(int value)
    {
        OnPropertyChanged(nameof(BackupStorageSummary));
    }

    partial void OnTotalBackupSizeChanged(string value)
    {
        OnPropertyChanged(nameof(BackupStorageSummary));
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifySwitchGameState();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SafetyHint));
        NotifySwitchGameState();
    }
}
