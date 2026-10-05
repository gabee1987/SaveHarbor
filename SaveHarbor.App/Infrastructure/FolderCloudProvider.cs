using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class FolderCloudProvider(IAppDataPathProvider pathProvider, IPlayerIdentity playerIdentity) : ICloudProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string ProviderName => "Local test cloud";

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath(game);
        Directory.CreateDirectory(rootPath);

        try
        {
            EnsureGameFolder(game);
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(new CloudConnectionStatus(false, ProviderName, rootPath, exception.Message));
        }

        return Task.FromResult(new CloudConnectionStatus(
            true,
            ProviderName,
            rootPath,
            "Local folder cloud provider is connected for sync testing."));
    }

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath(game);
        Directory.CreateDirectory(rootPath);

        try
        {
            EnsureGameFolder(game);
        }
        catch (InvalidOperationException exception)
        {
            var rejected = new CloudConnectionStatus(false, ProviderName, rootPath, exception.Message);
            return Task.FromResult(new CloudConnectionResult(false, rejected, exception.Message));
        }

        var status = new CloudConnectionStatus(true, ProviderName, rootPath, "Local folder cloud provider is connected.");
        return Task.FromResult(new CloudConnectionResult(true, status, status.Message));
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(game, worldId);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CloudWorldManifest>(stream, JsonOptions, cancellationToken);
        if (manifest is not null && !BelongsToGame(manifest, game))
        {
            throw new InvalidDataException($"The cloud manifest for this world belongs to another game, not {game}.");
        }

        return manifest;
    }

    public async Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var worldsPath = Path.Combine(GetRootPath(game), "worlds");
        if (!Directory.Exists(worldsPath))
        {
            return [];
        }

        var manifests = new List<CloudWorldManifest>();
        foreach (var manifestPath in Directory.EnumerateFiles(worldsPath, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<CloudWorldManifest>(stream, JsonOptions, cancellationToken);
            if (manifest?.LatestVersion is not null
                && !string.IsNullOrWhiteSpace(manifest.WorldId)
                && BelongsToGame(manifest, game))
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    public async Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var lockPath = GetSessionLockPath(game, worldId);
        if (!File.Exists(lockPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(lockPath);
        return await JsonSerializer.DeserializeAsync<CloudSessionLock>(stream, JsonOptions, cancellationToken);
    }

    public async Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.ArchivePath))
        {
            return new CloudUploadResult(false, null, "Upload archive was not found.");
        }

        var game = request.World.Game;
        EnsureGameFolder(game);

        var worldPath = GetWorldPath(game, request.World.WorldId);
        var versionsPath = Path.Combine(worldPath, "versions");
        Directory.CreateDirectory(versionsPath);

        var archiveTargetPath = SafePath.CombineUnderRoot(versionsPath, request.VersionMetadata.ArchiveFileName);
        var metadataTargetPath = Path.ChangeExtension(archiveTargetPath, ".json");

        File.Copy(request.ArchivePath, archiveTargetPath, overwrite: true);
        await File.WriteAllTextAsync(
            metadataTargetPath,
            JsonSerializer.Serialize(request.VersionMetadata, JsonOptions),
            cancellationToken);

        var manifest = request.PreviousManifest ?? new CloudWorldManifest
        {
            Provider = ProviderName,
            Game = game.ToStorageKey(),
            WorldId = request.World.WorldId,
            WorldName = request.World.WorldName
        };

        manifest.Provider = ProviderName;
        manifest.WorldId = request.World.WorldId;
        manifest.WorldName = request.World.WorldName;
        manifest.LatestVersion = request.VersionMetadata;
        manifest.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await WriteJsonAtomicAsync(GetManifestPath(game, request.World.WorldId), manifest, cancellationToken);

        return new CloudUploadResult(true, manifest, $"Uploaded {request.World.WorldName} v{request.VersionMetadata.VersionNumber}.");
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var versionsPath = Path.Combine(GetWorldPath(request.World.Game, request.World.WorldId), "versions");
        var sourcePath = SafePath.CombineUnderRoot(versionsPath, request.Version.ArchiveFileName);
        if (!File.Exists(sourcePath))
        {
            return Task.FromResult(new CloudDownloadResult(false, null, "Cloud archive was not found."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetArchivePath)!);
        File.Copy(sourcePath, request.TargetArchivePath, overwrite: true);
        return Task.FromResult(new CloudDownloadResult(true, request.TargetArchivePath, $"Downloaded {request.World.WorldName} v{request.Version.VersionNumber}."));
    }

    public Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var versionsPath = Path.Combine(GetWorldPath(game, worldId), "versions");
        IReadOnlyList<CloudStoredVersion> versions = Directory.Exists(versionsPath)
            ? Directory.EnumerateFiles(versionsPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(path => new CloudStoredVersion(Path.GetFileName(path)))
                .ToArray()
            : [];
        return Task.FromResult(versions);
    }

    public Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        var versionsPath = Path.Combine(GetWorldPath(game, worldId), "versions");
        var archivePath = SafePath.CombineUnderRoot(versionsPath, archiveFileName);
        File.Delete(Path.ChangeExtension(archivePath, ".json"));
        File.Delete(archivePath);
        return Task.CompletedTask;
    }

    public Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        EnsureGameFolder(game);
        return WriteJsonAtomicAsync(GetSessionLockPath(game, sessionLock.WorldId), sessionLock, cancellationToken);
    }

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        var lockPath = GetSessionLockPath(game, worldId);
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }

        return Task.CompletedTask;
    }

    private string GetRootPath(GameId game)
    {
        return pathProvider.GetLocalTestCloudRoot(game);
    }

    private string GetWorldPath(GameId game, string worldId)
    {
        return SafePath.CombineUnderRoot(Path.Combine(GetRootPath(game), "worlds"), worldId);
    }

    private string GetManifestPath(GameId game, string worldId)
    {
        return Path.Combine(GetWorldPath(game, worldId), "manifest.json");
    }

    private string GetSessionLockPath(GameId game, string worldId)
    {
        return Path.Combine(GetWorldPath(game, worldId), "locks", "active-session.json");
    }

    private static bool BelongsToGame(CloudWorldManifest manifest, GameId game)
    {
        return string.IsNullOrWhiteSpace(manifest.Game)
            || string.Equals(manifest.Game, game.ToStorageKey(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = $"{path}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
