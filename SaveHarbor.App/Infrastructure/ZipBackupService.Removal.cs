using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class ZipBackupService
{
    // A world is only deleted after a backup of it has been written and read back successfully. The game's own spare
    // copy (Dragonwilds' .sav.backup) is moved into the world's backup folder rather than deleted, so nothing is lost.
    public async Task<BackupInfo> RemoveWorldAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        var isFileSet = adapter.PayloadKind == WorldPayloadKind.FileSet;
        var payloadFiles = isFileSet ? adapter.GetPayloadFiles(world) : [];
        var exists = isFileSet ? payloadFiles.Count > 0 && payloadFiles.All(File.Exists) : Directory.Exists(world.SavePath);
        if (!exists)
        {
            throw new FileNotFoundException("The world's save files were not found, so nothing was removed.", world.SavePath);
        }

        var backup = await CreateBackupAsync(world, BackupReasons.PreRemove, cancellationToken);
        await VerifyArchiveAsync(backup.FilePath, world.Game, cancellationToken);

        if (adapter.GetGameBackupCopyPath(world) is { } gameCopy)
        {
            var keptCopy = Path.Combine(Path.GetDirectoryName(backup.FilePath)!, $"{Path.GetFileNameWithoutExtension(backup.FilePath)}_{Path.GetFileName(gameCopy)}");
            File.Move(gameCopy, keptCopy);
        }

        if (isFileSet)
        {
            foreach (var file in payloadFiles)
            {
                File.Delete(file);
            }
        }
        else
        {
            Directory.Delete(world.SavePath, recursive: true);
        }

        return backup;
    }

    // Keeps a SaveHarbor archive from elsewhere (a downloaded cloud version) as a backup of its world, after checking
    // that it belongs to this game and that its contents match the hash recorded inside it.
    public async Task<BackupInfo> StoreArchiveCopyAsync(string archivePath, GameId game, string reason, CancellationToken cancellationToken = default)
    {
        var manifest = await VerifyArchiveAsync(archivePath, game, cancellationToken);
        var worldName = string.IsNullOrWhiteSpace(manifest.WorldName) ? manifest.WorldId : manifest.WorldName;
        var folder = GetWorldBackupFolder(game, worldName);
        Directory.CreateDirectory(folder);

        var createdAt = DateTimeOffset.UtcNow;
        var targetPath = Enumerable.Range(1, MaxNameAttempts)
            .Select(attempt => Path.Combine(folder, BackupFileName.Create(createdAt, worldName, reason, attempt)))
            .FirstOrDefault(path => !File.Exists(path))
            ?? throw new IOException("Could not find a free backup file name.");

        var partialPath = targetPath + ".partial";
        try
        {
            File.Copy(archivePath, partialPath, overwrite: true);
            File.Move(partialPath, targetPath);
        }
        finally
        {
            File.Delete(partialPath);
        }

        var fileInfo = new FileInfo(targetPath);
        return new BackupInfo(fileInfo.FullName, fileInfo.Name, fileInfo.CreationTime, fileInfo.Length);
    }

    private async Task<BackupManifest> VerifyArchiveAsync(string archivePath, GameId game, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(archivePath, game, cancellationToken);
        await RunWithExtractedPayloadAsync(archivePath, payloadRoot =>
        {
            if (!string.IsNullOrWhiteSpace(manifest.PayloadSha256)
                && !string.Equals(DirectoryHashCalculator.ComputeSha256(payloadRoot), manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The backup could not be verified, so nothing was removed.");
            }
        }, cancellationToken);

        return manifest;
    }
}
