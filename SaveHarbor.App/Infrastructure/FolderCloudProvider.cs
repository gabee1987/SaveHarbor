using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class FolderCloudProvider : ICloudProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string rootPath;

    public FolderCloudProvider(IAppDataPathProvider pathProvider)
    {
        rootPath = pathProvider.LocalTestCloudRoot;
    }

    public string ProviderName => "Local test cloud";

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootPath);
        return Task.FromResult(new CloudConnectionStatus(
            true,
            ProviderName,
            rootPath,
            "Local folder cloud provider is connected for sync testing."));
    }

    public Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootPath);
        var status = new CloudConnectionStatus(true, ProviderName, rootPath, "Local folder cloud provider is connected.");
        return Task.FromResult(new CloudConnectionResult(true, status, status.Message));
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<CloudWorldManifest?> GetWorldManifestAsync(string worldId, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(worldId);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<CloudWorldManifest>(stream, JsonOptions, cancellationToken);
    }

    public async Task<CloudSessionLock?> GetSessionLockAsync(string worldId, CancellationToken cancellationToken = default)
    {
        var lockPath = GetSessionLockPath(worldId);
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

        var worldPath = GetWorldPath(request.World.WorldId);
        var versionsPath = Path.Combine(worldPath, "versions");
        Directory.CreateDirectory(versionsPath);

        var archiveTargetPath = Path.Combine(versionsPath, request.VersionMetadata.ArchiveFileName);
        var metadataTargetPath = Path.ChangeExtension(archiveTargetPath, ".json");

        File.Copy(request.ArchivePath, archiveTargetPath, overwrite: true);
        await File.WriteAllTextAsync(
            metadataTargetPath,
            JsonSerializer.Serialize(request.VersionMetadata, JsonOptions),
            cancellationToken);

        var manifest = request.PreviousManifest ?? new CloudWorldManifest
        {
            Provider = ProviderName,
            Game = "Windrose",
            WorldId = request.World.WorldId,
            WorldName = request.World.WorldName
        };

        manifest.Provider = ProviderName;
        manifest.WorldId = request.World.WorldId;
        manifest.WorldName = request.World.WorldName;
        manifest.LatestVersion = request.VersionMetadata;
        manifest.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await WriteJsonAtomicAsync(GetManifestPath(request.World.WorldId), manifest, cancellationToken);

        return new CloudUploadResult(true, manifest, $"Uploaded {request.World.WorldName} v{request.VersionMetadata.VersionNumber}.");
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.Combine(GetWorldPath(request.World.WorldId), "versions", request.Version.ArchiveFileName);
        if (!File.Exists(sourcePath))
        {
            return Task.FromResult(new CloudDownloadResult(false, null, "Cloud archive was not found."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetArchivePath)!);
        File.Copy(sourcePath, request.TargetArchivePath, overwrite: true);
        return Task.FromResult(new CloudDownloadResult(true, request.TargetArchivePath, $"Downloaded {request.World.WorldName} v{request.Version.VersionNumber}."));
    }

    public Task WriteSessionLockAsync(CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        return WriteJsonAtomicAsync(GetSessionLockPath(sessionLock.WorldId), sessionLock, cancellationToken);
    }

    public Task ClearSessionLockAsync(string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        var lockPath = GetSessionLockPath(worldId);
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }

        return Task.CompletedTask;
    }

    private string GetWorldPath(string worldId)
    {
        return Path.Combine(rootPath, "worlds", worldId);
    }

    private string GetManifestPath(string worldId)
    {
        return Path.Combine(GetWorldPath(worldId), "manifest.json");
    }

    private string GetSessionLockPath(string worldId)
    {
        return Path.Combine(GetWorldPath(worldId), "locks", "active-session.json");
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
