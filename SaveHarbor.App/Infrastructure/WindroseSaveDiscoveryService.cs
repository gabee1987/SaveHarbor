using System.Globalization;
using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindroseSaveDiscoveryService : IWindroseSaveDiscoveryService
{
    private const string DefaultRocksDbVersion = "0.10.0";
    private static readonly string[] RocksDbRootNames = ["RocksDB_v2", "RocksDB"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<WindroseWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var profilesRoot = GetProfilesRoot();

        if (!Directory.Exists(profilesRoot))
        {
            return [];
        }

        var worldFolders = Directory.EnumerateDirectories(profilesRoot)
            .Select(CreateProfile)
            .Where(profile => Directory.Exists(profile.WorldsPath))
            .SelectMany(profile => Directory.EnumerateDirectories(profile.WorldsPath))
            .ToArray();

        var worlds = new List<WindroseWorld>();
        foreach (var worldFolder in worldFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var world = await ReadWorldAsync(worldFolder, cancellationToken);
            if (world is not null)
            {
                worlds.Add(world);
            }
        }

        return worlds
            .OrderByDescending(world => world.LastModifiedAt)
            .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<IReadOnlyList<WindroseProfile>> DiscoverProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profilesRoot = GetProfilesRoot();

        if (!Directory.Exists(profilesRoot))
        {
            return Task.FromResult<IReadOnlyList<WindroseProfile>>([]);
        }

        var profiles = Directory.EnumerateDirectories(profilesRoot)
            .Select(CreateProfile)
            .OrderByDescending(profile => profile.LastModifiedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyList<WindroseProfile>>(profiles);
    }

    public async Task<WindroseWorld?> ReadWorldAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        var descriptionPath = Path.Combine(worldPath, "WorldDescription.json");
        if (!Directory.Exists(worldPath) || !File.Exists(descriptionPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(descriptionPath);
        var document = await JsonSerializer.DeserializeAsync<WorldDescriptionDocument>(stream, JsonOptions, cancellationToken);
        var description = document?.WorldDescription;
        if (description is null)
        {
            return null;
        }

        var directory = new DirectoryInfo(worldPath);
        var files = directory.EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        var lastModified = files.Length > 0
            ? files.Max(file => file.LastWriteTimeUtc)
            : directory.LastWriteTimeUtc;

        var worldId = !string.IsNullOrWhiteSpace(description.IslandId)
            ? description.IslandId
            : directory.Name;

        return new WindroseWorld(
            worldId,
            string.IsNullOrWhiteSpace(description.WorldName) ? directory.Name : description.WorldName,
            string.IsNullOrWhiteSpace(description.WorldPresetType) ? "Unknown" : description.WorldPresetType,
            directory.FullName,
            ConvertUnrealTimestamp(description.CreationTime),
            new DateTimeOffset(lastModified, TimeSpan.Zero).ToLocalTime(),
            files.Sum(file => file.Length),
            files.Length);
    }

    private static DateTimeOffset ConvertUnrealTimestamp(double creationTime)
    {
        if (creationTime <= 0)
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            var ticks = Convert.ToInt64(creationTime, CultureInfo.InvariantCulture);
            return new DateTimeOffset(ticks, TimeSpan.Zero).ToLocalTime();
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static string GetProfilesRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "R5", "Saved", "SaveProfiles");
    }

    private static WindroseProfile CreateProfile(string profilePath)
    {
        var profileDirectory = new DirectoryInfo(profilePath);
        var activeRoot = GetActiveRocksDbRoot(profilePath);
        var versionDirectory = activeRoot is not null
            ? GetLatestVersionDirectory(activeRoot)
            : null;

        var rocksDbVersion = versionDirectory?.Name ?? DefaultRocksDbVersion;
        var rocksDbRoot = activeRoot ?? Path.Combine(profilePath, RocksDbRootNames[0]);
        var worldsPath = Path.Combine(rocksDbRoot, rocksDbVersion, "Worlds");
        var lastModified = Directory.Exists(profilePath)
            ? profileDirectory.LastWriteTimeUtc
            : DateTime.UtcNow;

        return new WindroseProfile(
            profileDirectory.Name,
            profileDirectory.FullName,
            rocksDbVersion,
            worldsPath,
            new DateTimeOffset(lastModified, TimeSpan.Zero).ToLocalTime());
    }

    private static string? GetActiveRocksDbRoot(string profilePath)
    {
        return RocksDbRootNames
            .Select(rootName => Path.Combine(profilePath, rootName))
            .FirstOrDefault(Directory.Exists);
    }

    private static DirectoryInfo? GetLatestVersionDirectory(string rocksDbRoot)
    {
        return Directory.EnumerateDirectories(rocksDbRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .FirstOrDefault();
    }
}
