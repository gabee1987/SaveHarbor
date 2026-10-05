using System.Globalization;
using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

public sealed class WindroseSaveAdapter(GameOptionsProvider optionsProvider) : IGameSaveAdapter
{
    private const string DefaultRocksDbVersion = "0.10.0";
    private static readonly string[] RocksDbRootNames = ["RocksDB_v2", "RocksDB"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WorldPayloadKind PayloadKind => WorldPayloadKind.Directory;

    public string SaveRootPath => GetProfilesRoot();

    public async Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var profilesRoot = GetProfilesRoot();

        if (!Directory.Exists(profilesRoot))
        {
            return [];
        }

        var worldFolders = Directory.EnumerateDirectories(profilesRoot)
            .Select(CreateSaveRoot)
            .Where(profile => Directory.Exists(profile.WorldsPath))
            .SelectMany(profile => Directory.EnumerateDirectories(profile.WorldsPath))
            .ToArray();

        var worlds = new List<GameWorld>();
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

    public Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default)
    {
        var profilesRoot = GetProfilesRoot();

        if (!Directory.Exists(profilesRoot))
        {
            return Task.FromResult<IReadOnlyList<GameSaveRoot>>([]);
        }

        var profiles = Directory.EnumerateDirectories(profilesRoot)
            .Select(CreateSaveRoot)
            .OrderByDescending(profile => profile.LastModifiedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyList<GameSaveRoot>>(profiles);
    }

    public async Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default)
    {
        var descriptionPath = Path.Combine(savePath, "WorldDescription.json");
        if (!Directory.Exists(savePath) || !File.Exists(descriptionPath))
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

        var directory = new DirectoryInfo(savePath);
        var files = directory.EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        var lastModified = files.Length > 0
            ? files.Max(file => file.LastWriteTimeUtc)
            : directory.LastWriteTimeUtc;

        var worldId = !string.IsNullOrWhiteSpace(description.IslandId)
            ? description.IslandId
            : directory.Name;

        return new GameWorld(
            GameId.Windrose,
            worldId,
            string.IsNullOrWhiteSpace(description.WorldName) ? directory.Name : description.WorldName,
            string.IsNullOrWhiteSpace(description.WorldPresetType) ? "Unknown" : description.WorldPresetType,
            directory.FullName,
            ConvertUnrealTimestamp(description.CreationTime),
            new DateTimeOffset(lastModified, TimeSpan.Zero).ToLocalTime(),
            files.Sum(file => file.Length),
            files.Length);
    }

    // The Windrose view does not show extra world facts yet; its reskin can add them here.
    public Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorldFact>>([]);

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world)
    {
        return Directory.EnumerateFiles(world.SavePath, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "LOCK", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public string GetExpectedWorldPath(GameSaveRoot root, string worldId) =>
        SafePath.CombineUnderRoot(root.WorldsPath, worldId);

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

    private string GetProfilesRoot()
    {
        var overrideRoot = optionsProvider.Get(GameId.Windrose).SaveRoot;
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Environment.ExpandEnvironmentVariables(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "R5", "Saved", "SaveProfiles");
    }

    private static GameSaveRoot CreateSaveRoot(string profilePath)
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

        return new GameSaveRoot(
            GameId.Windrose,
            profileDirectory.Name,
            profileDirectory.FullName,
            worldsPath,
            $"RocksDB {rocksDbVersion}",
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
