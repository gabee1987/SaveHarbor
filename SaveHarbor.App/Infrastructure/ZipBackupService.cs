using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class ZipBackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string BackupRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveHarbor",
        "backups");

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(BackupRoot))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        }

        return Task.Run<IReadOnlyList<BackupInfo>>(() =>
        {
            return Directory.EnumerateFiles(BackupRoot, "*.zip", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var fileInfo = new FileInfo(path);
                    return new BackupInfo(
                        fileInfo.FullName,
                        fileInfo.Name,
                        new DateTimeOffset(fileInfo.CreationTime),
                        fileInfo.Length);
                })
                .OrderByDescending(backup => backup.CreatedAt)
                .ToArray();
        }, cancellationToken);
    }

    public async Task<BackupInfo> CreateBackupAsync(WindroseWorld world, string reason, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupRoot);

        var safeWorldName = MakeSafeFileName(world.WorldName);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{timestamp}_{safeWorldName}_{reason}.zip";
        var targetPath = Path.Combine(BackupRoot, fileName);

        await Task.Run(() => CreateArchive(world, targetPath, reason, cancellationToken), cancellationToken);

        var fileInfo = new FileInfo(targetPath);
        return new BackupInfo(fileInfo.FullName, fileInfo.Name, fileInfo.CreationTime, fileInfo.Length);
    }

    public async Task RestoreBackupAsync(string backupPath, WindroseWorld targetWorld, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup archive was not found.", backupPath);
        }

        await CreateBackupAsync(targetWorld, "pre-restore", cancellationToken);

        var targetPath = targetWorld.SavePath;
        var tempPath = Path.Combine(Path.GetTempPath(), "SaveHarbor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            ZipFile.ExtractToDirectory(backupPath, tempPath);
            var payloadRoot = Path.Combine(tempPath, "world");
            if (!Directory.Exists(payloadRoot))
            {
                throw new InvalidOperationException("Backup archive does not contain a world payload.");
            }

            await Task.Run(() =>
            {
                ReplaceDirectory(payloadRoot, targetPath, cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    private static void CreateArchive(WindroseWorld world, string targetPath, string reason, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "SaveHarbor", Guid.NewGuid().ToString("N"));
        var payloadRoot = Path.Combine(tempPath, "world");
        Directory.CreateDirectory(payloadRoot);

        try
        {
            CopyDirectory(world.SavePath, payloadRoot, cancellationToken);

            var manifest = new
            {
                SchemaVersion = 1,
                Game = "Windrose",
                world.WorldId,
                world.WorldName,
                SourcePath = world.SavePath,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Reason = reason,
                FileCount = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).Count(),
                PayloadSha256 = ComputeDirectoryHash(payloadRoot)
            };

            var manifestPath = Path.Combine(tempPath, "saveharbor-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            ZipFile.CreateFromDirectory(tempPath, targetPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
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

    private static string ComputeDirectoryHash(string root)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.OrdinalIgnoreCase))
        {
            var relativePathBytes = System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/'));
            sha.TransformBlock(relativePathBytes, 0, relativePathBytes.Length, null, 0);
            var bytes = File.ReadAllBytes(file);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []);
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", value.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
