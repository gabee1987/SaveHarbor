using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

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
        ReplaceDirectory(payloadRoot, target.SavePath, cancellationToken);
    }

    public string Import(string payloadRoot, BackupManifest manifest, GameSaveRoot root, IGameSaveAdapter adapter, bool overwriteExisting, CancellationToken cancellationToken)
    {
        var targetWorldPath = adapter.GetExpectedWorldPath(root, manifest.WorldId);
        if (Directory.Exists(targetWorldPath) && !overwriteExisting)
        {
            throw new InvalidOperationException("This world already exists on this computer. Use restore instead, or confirm overwrite.");
        }

        Directory.CreateDirectory(root.WorldsPath);

        if (Directory.Exists(targetWorldPath))
        {
            Directory.Delete(targetWorldPath, true);
        }

        CopyDirectory(payloadRoot, targetWorldPath, cancellationToken);
        return targetWorldPath;
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

    private static void ReplaceDirectory(string source, string target, CancellationToken cancellationToken)
    {
        var stagingPath = $"{target}.saveharbor-staging-{Guid.NewGuid():N}";
        CopyDirectory(source, stagingPath, cancellationToken);

        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }

        Directory.Move(stagingPath, target);
    }
}
