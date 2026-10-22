using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud;

// The overview as last read from the cloud, with the group or folder it belongs to.
public sealed record CachedCloudOverview(string Scope, DateTimeOffset CachedAtUtc, CloudWorldOverview Overview);

// Keeps the last cloud overview per provider and game, so the window shows the worlds' sharing state right after
// start while the live one loads. Only for display: uploads, downloads and locks always read the cloud itself.
// Only what the window shows is kept: world names, version numbers, who shared them and when.
public sealed class CloudSnapshotCache(IAppDataPathProvider paths, IAppLogger logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Null when nothing is cached for this scope (another group or folder, or a file that cannot be read).
    public CachedCloudOverview? Load(string providerKey, GameId game, string scope)
    {
        var path = GetPath(providerKey, game);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var cached = JsonSerializer.Deserialize<CachedCloudOverview>(File.ReadAllText(path), JsonOptions);
            return cached is { Overview: not null } && string.Equals(cached.Scope, scope, StringComparison.Ordinal) ? cached : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Warning(AppLogKeyword.CloudSync, "The cached cloud overview could not be read: {Message}", ex.Message);
            return null;
        }
    }

    public void Save(string providerKey, GameId game, string scope, CloudWorldOverview overview, DateTimeOffset cachedAtUtc)
    {
        var path = GetPath(providerKey, game);
        var cached = new CachedCloudOverview(scope, cachedAtUtc, Minimal(overview));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cached, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning(AppLogKeyword.CloudSync, "The cloud overview could not be cached: {Message}", ex.Message);
        }
    }

    // After sign-out or when the provider is no longer connected, nothing of the group stays on this PC.
    public void Remove(string providerKey, GameId game)
    {
        try
        {
            File.Delete(GetPath(providerKey, game));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning(AppLogKeyword.CloudSync, "The cached cloud overview could not be removed: {Message}", ex.Message);
        }
    }

    private string GetPath(string providerKey, GameId game) =>
        Path.Combine(paths.CloudCacheRoot, providerKey, game.ToStorageKey() + ".json");

    private static CloudWorldOverview Minimal(CloudWorldOverview overview) => new(
        overview.IsConnected,
        overview.LocalWorlds.Select(summary => summary with { LatestVersion = Minimal(summary.LatestVersion) }).ToArray(),
        overview.CloudOnlyWorlds.Select(manifest => new CloudWorldManifest
        {
            Game = manifest.Game,
            WorldId = manifest.WorldId,
            WorldName = manifest.WorldName,
            LatestVersion = Minimal(manifest.LatestVersion),
            UpdatedAtUtc = manifest.UpdatedAtUtc
        }).ToArray());

    private static CloudVersionMetadata? Minimal(CloudVersionMetadata? version) => version is null ? null : new CloudVersionMetadata
    {
        VersionNumber = version.VersionNumber,
        UploadedAtUtc = version.UploadedAtUtc,
        UploadedBy = version.UploadedBy
    };
}
