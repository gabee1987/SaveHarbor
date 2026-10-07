using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IBackupService
{
    string GetBackupRoot(GameId game);
    string GetWorldBackupFolder(GameId game, string worldName);
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(GameId game, CancellationToken cancellationToken = default);
    Task<BackupManifest> ReadManifestAsync(string backupPath, GameId expectedGame, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateBackupAsync(GameWorld world, string reason, CancellationToken cancellationToken = default);
    Task<string> ImportBackupAsNewWorldAsync(string backupPath, GameSaveRoot profile, bool overwriteExisting, CancellationToken cancellationToken = default);
    Task<BackupInfo> CreateImportSnapshotAsync(ImportCandidate candidate, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupPath, GameWorld targetWorld, CancellationToken cancellationToken = default);

    // Backs the world up, verifies the backup, then deletes the world's save files. Returns the backup.
    Task<BackupInfo> RemoveWorldAsync(GameWorld world, CancellationToken cancellationToken = default);

    Task<BackupInfo> StoreArchiveCopyAsync(string archivePath, GameId game, string reason, CancellationToken cancellationToken = default);
}
