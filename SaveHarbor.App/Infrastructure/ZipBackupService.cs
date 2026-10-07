using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Backup;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class ZipBackupService(IAppDataPathProvider pathProvider, IGameRegistry gameRegistry, IAppSettingsStore settings) : IBackupService
{
    private const int MaxNameAttempts = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly DirectoryPayloadStrategy DirectoryStrategy = new();
    private static readonly FileSetPayloadStrategy FileSetStrategy = new();

    public string GetBackupRoot(GameId game) => pathProvider.GetBackupRoot(game);

    public string GetWorldBackupFolder(GameId game, string worldName) =>
        Path.Combine(GetBackupRoot(game), BackupFileName.FolderName(worldName));

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var backupRoot = GetBackupRoot(game);
        if (!Directory.Exists(backupRoot))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        }

        return Task.Run<IReadOnlyList<BackupInfo>>(() =>
        {
            // Backups made before world folders existed may still sit directly in the root.
            return Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(BackupFileName.Extension, StringComparison.OrdinalIgnoreCase))
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

    public async Task<BackupManifest> ReadManifestAsync(string backupPath, GameId expectedGame, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup archive was not found.", backupPath);
        }

        await using var stream = File.OpenRead(backupPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("saveharbor-manifest.json")
            ?? throw new InvalidOperationException("This archive is missing the SaveHarbor manifest.");

        await using var manifestStream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken);
        if (manifest is null || !string.Equals(manifest.Game, expectedGame.ToStorageKey(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"This backup does not look like a {expectedGame} SaveHarbor backup.");
        }

        if (manifest.SchemaVersion is not (1 or 2))
        {
            throw new InvalidOperationException("Unsupported backup version.");
        }

        if (string.IsNullOrWhiteSpace(manifest.WorldId))
        {
            throw new InvalidOperationException("This backup manifest does not contain a world id.");
        }

        return manifest;
    }

    public async Task<BackupInfo> CreateBackupAsync(GameWorld world, string reason, CancellationToken cancellationToken = default)
    {
        var backupRoot = GetWorldBackupFolder(world.Game, world.WorldName);
        Directory.CreateDirectory(backupRoot);

        var createdAt = DateTimeOffset.UtcNow;
        var targetPath = Enumerable.Range(1, MaxNameAttempts)
            .Select(attempt => Path.Combine(backupRoot, BackupFileName.Create(createdAt, world.WorldName, reason, attempt)))
            .FirstOrDefault(path => !File.Exists(path))
            ?? throw new IOException("Could not find a free backup file name.");

        await Task.Run(() => CreateArchive(world, targetPath, reason, cancellationToken), cancellationToken);
        await PruneOldBackupsAsync(world.Game, targetPath, cancellationToken);

        var fileInfo = new FileInfo(targetPath);
        return new BackupInfo(fileInfo.FullName, fileInfo.Name, fileInfo.CreationTime, fileInfo.Length);
    }

    public async Task RestoreBackupAsync(string backupPath, GameWorld targetWorld, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup archive was not found.", backupPath);
        }

        var manifest = await ReadManifestAsync(backupPath, targetWorld.Game, cancellationToken);
        var strategy = SelectStrategyForManifest(manifest, gameRegistry.Get(targetWorld.Game).SaveAdapter);

        await CreateBackupAsync(targetWorld, BackupReasons.PreRestore, cancellationToken);

        await RunWithExtractedPayloadAsync(backupPath, payloadRoot =>
            strategy.Restore(payloadRoot, targetWorld, manifest, cancellationToken), cancellationToken);
    }

    private async Task PruneOldBackupsAsync(GameId game, string justCreatedPath, CancellationToken cancellationToken)
    {
        var backups = await ListBackupsAsync(game, cancellationToken);
        foreach (var backup in BackupRetention.SelectForDeletion(backups, settings.Current.BackupRetentionCount, justCreatedPath, DateTimeOffset.Now))
        {
            File.Delete(backup.FilePath);
        }
    }

    private static IPayloadStrategy SelectStrategy(WorldPayloadKind kind) =>
        kind == WorldPayloadKind.FileSet ? FileSetStrategy : DirectoryStrategy;

    private static IPayloadStrategy SelectStrategyForManifest(BackupManifest manifest, IGameSaveAdapter adapter)
    {
        if (!Enum.TryParse<WorldPayloadKind>(manifest.PayloadKind, ignoreCase: true, out var manifestKind) || manifestKind != adapter.PayloadKind)
        {
            throw new InvalidOperationException("This backup was created for a different save layout.");
        }

        return SelectStrategy(manifestKind);
    }

    private static async Task RunWithExtractedPayloadAsync(string backupPath, Action<string> apply, CancellationToken cancellationToken)
    {
        var tempPath = CreateTempFolder();
        try
        {
            ZipFile.ExtractToDirectory(backupPath, tempPath);
            var payloadRoot = Path.Combine(tempPath, "world");
            if (!Directory.Exists(payloadRoot))
            {
                throw new InvalidOperationException("Backup archive does not contain a world payload.");
            }

            await Task.Run(() => apply(payloadRoot), cancellationToken);
        }
        finally
        {
            DeleteTempFolder(tempPath);
        }
    }

    private static string CreateTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "SaveHarbor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private void CreateArchive(GameWorld world, string targetPath, string reason, CancellationToken cancellationToken)
    {
        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        var strategy = SelectStrategy(adapter.PayloadKind);

        var tempPath = CreateTempFolder();
        var payloadRoot = Path.Combine(tempPath, "world");
        var partialPath = targetPath + ".partial";
        Directory.CreateDirectory(payloadRoot);

        try
        {
            var files = strategy.Stage(world, adapter, payloadRoot, cancellationToken);

            var manifest = new BackupManifest
            {
                SchemaVersion = 2,
                Game = world.Game.ToStorageKey(),
                WorldId = world.WorldId,
                WorldName = world.WorldName,
                SourcePath = world.SavePath,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Reason = reason,
                FileCount = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).Count(),
                PayloadSha256 = DirectoryHashCalculator.ComputeSha256(payloadRoot),
                PayloadKind = adapter.PayloadKind.ToString(),
                Files = [.. files]
            };

            var manifestPath = Path.Combine(tempPath, "saveharbor-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

            // Written next to the target first, so a failed or interrupted backup never leaves a broken archive.
            File.Delete(partialPath);
            ZipFile.CreateFromDirectory(tempPath, partialPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(partialPath, targetPath);
        }
        finally
        {
            File.Delete(partialPath);
            DeleteTempFolder(tempPath);
        }
    }
}
