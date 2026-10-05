using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IBackupService
{
    string BackupRoot { get; }
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupManifest> ReadManifestAsync(string backupPath, GameId expectedGame, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(GameWorld world, string reason, CancellationToken cancellationToken = default);
    Task<string> ImportBackupAsNewWorldAsync(string backupPath, GameSaveRoot profile, bool overwriteExisting, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupPath, GameWorld targetWorld, CancellationToken cancellationToken = default);
}
