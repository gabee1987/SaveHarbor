using System.IO;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class ZipBackupService
{
    // A world that would be replaced is always backed up first ("pre-import"). If it cannot be read, nothing is changed.
    public async Task<string> ImportBackupAsNewWorldAsync(
        string backupPath,
        GameSaveRoot profile,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(backupPath, profile.Game, cancellationToken);
        var adapter = gameRegistry.Get(profile.Game).SaveAdapter;
        var strategy = SelectStrategyForManifest(manifest, adapter);

        var targetPath = adapter.GetExpectedWorldPath(profile, manifest.WorldId);
        if (overwriteExisting && (File.Exists(targetPath) || Directory.Exists(targetPath)))
        {
            var existing = await adapter.ReadWorldAsync(targetPath, cancellationToken)
                ?? throw new InvalidDataException("The world that would be replaced could not be read, so no safety backup could be made. Nothing was changed.");
            await CreateBackupAsync(existing, BackupReasons.PreImport, cancellationToken);
        }

        var importedPath = string.Empty;
        await RunWithExtractedPayloadAsync(backupPath, payloadRoot =>
            importedPath = strategy.Import(payloadRoot, manifest, profile, adapter, overwriteExisting, cancellationToken), cancellationToken);

        return importedPath;
    }

    // Turns a world picked from outside the save folder into a normal SaveHarbor backup ("imported"), so the original
    // file is kept and the import itself goes through the same hash-checked, roll-back-safe path as any restore.
    // A single save file is copied first under its import name and validated again, so a file that changes or is
    // swapped after the user confirmed is rejected.
    public async Task<BackupInfo> CreateImportSnapshotAsync(ImportCandidate candidate, CancellationToken cancellationToken = default)
    {
        var adapter = gameRegistry.Get(candidate.World.Game).SaveAdapter;
        if (adapter.PayloadKind != WorldPayloadKind.FileSet)
        {
            return await CreateBackupAsync(candidate.World, BackupReasons.Imported, cancellationToken);
        }

        var tempPath = CreateTempFolder();
        try
        {
            var stagingRoot = new GameSaveRoot(candidate.World.Game, "import", tempPath, tempPath, string.Empty, DateTimeOffset.Now);
            var copyPath = adapter.GetExpectedWorldPath(stagingRoot, candidate.World.WorldId);
            File.Copy(candidate.World.SavePath, copyPath);

            var verified = await adapter.ReadImportCandidateAsync(copyPath, cancellationToken);
            if (verified is null
                || !string.Equals(verified.Fingerprint, candidate.Fingerprint, StringComparison.Ordinal)
                || verified.Revision != candidate.Revision)
            {
                throw new InvalidDataException("The file changed while it was being imported. Nothing was changed.");
            }

            return await CreateBackupAsync(candidate.World with { SavePath = copyPath }, BackupReasons.Imported, cancellationToken);
        }
        finally
        {
            DeleteTempFolder(tempPath);
        }
    }
}
