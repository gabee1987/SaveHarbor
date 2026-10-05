using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class StubBackupService : IBackupService
{
    public string GetBackupRoot(GameId game) => throw new NotSupportedException();

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(GameId game, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BackupManifest> ReadManifestAsync(string backupPath, GameId expectedGame, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BackupInfo> CreateBackupAsync(GameWorld world, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> ImportBackupAsNewWorldAsync(string backupPath, GameSaveRoot profile, bool overwriteExisting, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RestoreBackupAsync(string backupPath, GameWorld targetWorld, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
