using System.IO;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    public string BackupRoot => _backupService.BackupRoot;

    public string LocalSaveRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "R5",
        "Saved",
        "SaveProfiles");

    public string SelectedWorldSize => SelectedWorld is null ? "Unknown" : DisplayFormatter.FormatBytes(SelectedWorld.SizeBytes);

    public string SelectedWorldFileCount => SelectedWorld is null ? "0 files" : $"{SelectedWorld.FileCount:N0} files";

    public string LatestBackupSummary => LastBackup is null
        ? "No backups found"
        : $"{LastBackup.FileName} • {DisplayFormatter.FormatBytes(LastBackup.SizeBytes)}";

    public string LatestBackupFileName => LastBackup?.FileName ?? "No backups found";

    public string LatestBackupPath => LastBackup?.FilePath ?? "Create a backup to see the latest backup path here.";

    public string LatestBackupAge => LastBackup is null
        ? "Create a backup before sharing or restoring this world."
        : $"Created {DisplayFormatter.FormatAge(LastBackup.CreatedAt)}";

    public string LatestBackupDetails => LastBackup is null
        ? "No backup has been created yet."
        : $"{DisplayFormatter.FormatBytes(LastBackup.SizeBytes)} • {LatestBackupAge}";

    public string LatestBackupHeader => LastBackup is null
        ? "Latest backup • No backup has been created yet."
        : $"Latest backup • {LatestBackupDetails}";

    public string BackupStorageSummary => $"{BackupCount:N0} backups • {TotalBackupSize}";

    public string SelectedWorldModifiedAge => SelectedWorld is null
        ? "No world selected"
        : $"Modified {DisplayFormatter.FormatAge(SelectedWorld.LastModifiedAt)}";

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
        ? "None"
        : string.Equals(CloudStatus.SessionLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? "You"
            : CloudStatus.SessionLock.PlayerName;

    public string CloudSessionTooltip => CloudStatus?.SessionLock is null
        ? "No active cloud session. Nobody has marked this world as currently being played."
        : $"{CloudStatus.SessionLock.PlayerName} started a cloud session from v{CloudStatus.SessionLock.BasedOnVersionNumber} on {CloudStatus.SessionLock.StartedAtUtc:yyyy-MM-dd HH:mm} UTC. Lock expires at {CloudStatus.SessionLock.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC.";
}
