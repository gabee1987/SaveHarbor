using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IBackupService
{
    string BackupRoot { get; }
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(WindroseWorld world, string reason, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupPath, WindroseWorld targetWorld, CancellationToken cancellationToken = default);
}
