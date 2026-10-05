using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Backup;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed class ZipBackupService(IAppDataPathProvider pathProvider, IGameRegistry gameRegistry) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly DirectoryPayloadStrategy DirectoryStrategy = new();
    private static readonly FileSetPayloadStrategy FileSetStrategy = new();

    public string GetBackupRoot(GameId game) => pathProvider.GetBackupRoot(game);

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var backupRoot = GetBackupRoot(game);
        if (!Directory.Exists(backupRoot))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        }

        return Task.Run<IReadOnlyList<BackupInfo>>(() =>
        {
            return Directory.EnumerateFiles(backupRoot, "*.zip", SearchOption.TopDirectoryOnly)
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
        var backupRoot = GetBackupRoot(world.Game);
        Directory.CreateDirectory(backupRoot);

        var safeWorldName = FileNameSanitizer.MakeSafeFileName(world.WorldName);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{timestamp}_{safeWorldName}_{reason}.zip";
        var targetPath = Path.Combine(backupRoot, fileName);

        await Task.Run(() => CreateArchive(world, targetPath, reason, cancellationToken), cancellationToken);

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

        await CreateBackupAsync(targetWorld, "pre-restore", cancellationToken);

        await RunWithExtractedPayloadAsync(backupPath, payloadRoot =>
            strategy.Restore(payloadRoot, targetWorld, manifest, cancellationToken), cancellationToken);
    }

    public async Task<string> ImportBackupAsNewWorldAsync(
        string backupPath,
        GameSaveRoot profile,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(backupPath, profile.Game, cancellationToken);
        var adapter = gameRegistry.Get(profile.Game).SaveAdapter;
        var strategy = SelectStrategyForManifest(manifest, adapter);

        var importedPath = string.Empty;
        await RunWithExtractedPayloadAsync(backupPath, payloadRoot =>
            importedPath = strategy.Import(payloadRoot, manifest, profile, adapter, overwriteExisting, cancellationToken), cancellationToken);

        return importedPath;
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

            await Task.Run(() => apply(payloadRoot), cancellationToken);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    private void CreateArchive(GameWorld world, string targetPath, string reason, CancellationToken cancellationToken)
    {
        var adapter = gameRegistry.Get(world.Game).SaveAdapter;
        var strategy = SelectStrategy(adapter.PayloadKind);

        var tempPath = Path.Combine(Path.GetTempPath(), "SaveHarbor", Guid.NewGuid().ToString("N"));
        var payloadRoot = Path.Combine(tempPath, "world");
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
}
