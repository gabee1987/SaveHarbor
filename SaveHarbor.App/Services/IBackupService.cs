using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IBackupService
{
    string BackupRoot { get; }
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupManifest> ReadManifestAsync(string backupPath, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(WindroseWorld world, string reason, CancellationToken cancellationToken = default);
    Task<string> ImportBackupAsNewWorldAsync(string backupPath, WindroseProfile profile, bool overwriteExisting, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupPath, WindroseWorld targetWorld, CancellationToken cancellationToken = default);
}
