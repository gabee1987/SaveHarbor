using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Backup;

internal sealed class FileSetPayloadStrategy : IPayloadStrategy
{
    private const string TempSuffix = ".saveharbor-tmp";
    private const string PreviousSuffix = ".saveharbor-prev";

    private readonly record struct FileSwap(string Source, string Target);

    private readonly record struct AppliedSwap(string Target, string? Previous);

    public IReadOnlyList<BackupFileEntry> Stage(GameWorld world, IGameSaveAdapter adapter, string payloadRoot, CancellationToken cancellationToken)
    {
        var entries = new List<BackupFileEntry>();
        foreach (var file in adapter.GetPayloadFiles(world))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            var staged = Path.Combine(payloadRoot, name);
            File.Copy(file, staged, overwrite: true);
            entries.Add(new BackupFileEntry
            {
                RelativePath = name,
                Sha256 = FileHashCalculator.ComputeSha256(staged),
                SizeBytes = new FileInfo(staged).Length
            });
        }

        return entries;
    }

    public void Restore(string payloadRoot, GameWorld target, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var entries = ValidateEntries(payloadRoot, manifest, Path.GetExtension(target.SavePath));
        if (entries.Count == 1 && !string.Equals(entries[0].RelativePath, Path.GetFileName(target.SavePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This backup belongs to a world file with a different name.");
        }

        var targetDirectory = Path.GetDirectoryName(target.SavePath)!;
        var swaps = entries
            .Select(entry => new FileSwap(Path.Combine(payloadRoot, entry.RelativePath), SafePath.CombineUnderRoot(targetDirectory, entry.RelativePath)))
            .ToArray();

        ApplyAll(swaps, cancellationToken);
    }

    public string Import(string payloadRoot, BackupManifest manifest, GameSaveRoot root, IGameSaveAdapter adapter, bool overwriteExisting, CancellationToken cancellationToken)
    {
        var expectedPath = adapter.GetExpectedWorldPath(root, manifest.WorldId);
        var entries = ValidateEntries(payloadRoot, manifest, Path.GetExtension(expectedPath));

        Directory.CreateDirectory(root.WorldsPath);

        var swaps = entries
            .Select(entry => new FileSwap(Path.Combine(payloadRoot, entry.RelativePath), SafePath.CombineUnderRoot(root.WorldsPath, entry.RelativePath)))
            .ToArray();

        foreach (var swap in swaps)
        {
            if (File.Exists(swap.Target) && !overwriteExisting)
            {
                throw new IOException($"A local world file named '{Path.GetFileName(swap.Target)}' already exists.");
            }
        }

        ApplyAll(swaps, cancellationToken);
        return swaps[0].Target;
    }

    private static IReadOnlyList<BackupFileEntry> ValidateEntries(string payloadRoot, BackupManifest manifest, string requiredExtension)
    {
        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The backup does not list any world files.");
        }

        foreach (var entry in manifest.Files)
        {
            if (!SafePath.IsSafeSegment(entry.RelativePath)
                || !string.Equals(Path.GetExtension(entry.RelativePath), requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The backup contains an invalid world file name.");
            }

            var source = Path.Combine(payloadRoot, entry.RelativePath);
            if (!File.Exists(source))
            {
                throw new InvalidDataException("The backup is missing a world file listed in its manifest.");
            }

            if (!string.Equals(FileHashCalculator.ComputeSha256(source), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A world file in the backup does not match its recorded hash. Nothing was changed.");
            }
        }

        return manifest.Files;
    }

    private static void ApplyAll(IReadOnlyList<FileSwap> swaps, CancellationToken cancellationToken)
    {
        var applied = new List<AppliedSwap>();
        try
        {
            foreach (var swap in swaps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                applied.Add(new AppliedSwap(swap.Target, ReplaceFile(swap.Source, swap.Target)));
            }
        }
        catch
        {
            Rollback(applied);
            throw;
        }

        foreach (var swap in applied.Where(item => item.Previous is not null))
        {
            File.Delete(swap.Previous!);
        }
    }

    private static string? ReplaceFile(string source, string target)
    {
        var temp = target + TempSuffix;
        var previous = target + PreviousSuffix;
        var hadTarget = File.Exists(target);
        File.Copy(source, temp, overwrite: true);
        try
        {
            if (hadTarget)
            {
                File.Replace(temp, target, previous);
            }
            else
            {
                File.Move(temp, target);
            }

            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
            return hadTarget ? previous : null;
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            if (hadTarget && File.Exists(previous))
            {
                File.Move(previous, target, overwrite: true);
            }
            else if (!hadTarget && File.Exists(target))
            {
                File.Delete(target);
            }

            throw;
        }
    }

    private static void Rollback(IEnumerable<AppliedSwap> applied)
    {
        foreach (var swap in applied)
        {
            if (swap.Previous is not null && File.Exists(swap.Previous))
            {
                File.Move(swap.Previous, swap.Target, overwrite: true);
            }
            else if (swap.Previous is null && File.Exists(swap.Target))
            {
                File.Delete(swap.Target);
            }
        }
    }
}
