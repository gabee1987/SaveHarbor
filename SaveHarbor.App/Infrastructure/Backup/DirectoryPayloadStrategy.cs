using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Backup;

internal sealed class DirectoryPayloadStrategy : IPayloadStrategy
{
    public IReadOnlyList<BackupFileEntry> Stage(GameWorld world, IGameSaveAdapter adapter, string payloadRoot, CancellationToken cancellationToken)
    {
        CopyDirectory(world.SavePath, payloadRoot, cancellationToken);
        return [];
    }

    public void Restore(string payloadRoot, GameWorld target, BackupManifest manifest, CancellationToken cancellationToken)
    {
        VerifyPayload(payloadRoot, manifest);
        ReplaceDirectory(payloadRoot, target.SavePath, cancellationToken);
    }

    public string Import(string payloadRoot, BackupManifest manifest, GameSaveRoot root, IGameSaveAdapter adapter, bool overwriteExisting, CancellationToken cancellationToken)
    {
        var targetWorldPath = adapter.GetExpectedWorldPath(root, manifest.WorldId);
        if (Directory.Exists(targetWorldPath) && !overwriteExisting)
        {
            throw new InvalidOperationException("This world already exists on this computer. Use restore instead, or confirm overwrite.");
        }

        VerifyPayload(payloadRoot, manifest);
        Directory.CreateDirectory(root.WorldsPath);
        ReplaceDirectory(payloadRoot, targetWorldPath, cancellationToken);
        return targetWorldPath;
    }

    // Schema 2 manifests record the payload hash at backup time; older manifests are accepted as before.
    private static void VerifyPayload(string payloadRoot, BackupManifest manifest)
    {
        if (manifest.SchemaVersion >= 2
            && !string.IsNullOrWhiteSpace(manifest.PayloadSha256)
            && !string.Equals(DirectoryHashCalculator.ComputeSha256(payloadRoot), manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The world files in the backup do not match their recorded hash. Nothing was changed.");
        }
    }

    private static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(file), "LOCK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    // The new folder is fully copied before the old one is touched. The old folder is moved aside, not deleted, until
    // the new one is in place, and is moved back if that fails.
    private static void ReplaceDirectory(string source, string target, CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var stagingPath = $"{target}.saveharbor-staging-{suffix}";
        var previousPath = $"{target}.saveharbor-prev-{suffix}";
        try
        {
            CopyDirectory(source, stagingPath, cancellationToken);
        }
        catch
        {
            DeleteIfExists(stagingPath);
            throw;
        }

        var hadTarget = Directory.Exists(target);
        if (hadTarget)
        {
            Directory.Move(target, previousPath);
        }

        try
        {
            Directory.Move(stagingPath, target);
        }
        catch
        {
            if (hadTarget)
            {
                Directory.Move(previousPath, target);
            }

            DeleteIfExists(stagingPath);
            throw;
        }

        DeleteIfExists(previousPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
