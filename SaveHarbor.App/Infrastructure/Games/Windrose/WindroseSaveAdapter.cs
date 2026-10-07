using System.Globalization;
using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

public sealed class WindroseSaveAdapter(GameOptionsProvider optionsProvider, IAppDataPathProvider pathProvider) : IGameSaveAdapter
{
    public const string DescriptionFileName = "WorldDescription.json";
    private const long MaxDescriptionBytes = 1024 * 1024;
    private const string DefaultRocksDbVersion = "0.10.0";
    private static readonly string[] RocksDbRootNames = ["RocksDB_v2", "RocksDB"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WindroseSaveHealth health = new(pathProvider);

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
            .Where(folder => !WindrosePaths.IsSaveHarborFolder(folder))
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
        var descriptionPath = Path.Combine(savePath, DescriptionFileName);
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

    public async Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var settings = await WindroseWorldSettings.ReadAsync(Path.Combine(world.SavePath, DescriptionFileName), cancellationToken);
        return WindroseWorldFacts.From(world, settings, WindrosePaths.FormatVersion(world.SavePath));
    }

    public Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        WindroseWorldInspector.InspectAsync(world, cancellationToken);

    // A Windrose world is a folder; it is picked through the WorldDescription.json file inside it (or given as the folder).
    public string ImportFileFilter => $"Windrose world folder ({DescriptionFileName})|{DescriptionFileName}";

    public async Task<ImportCandidate?> ReadImportCandidateAsync(string path, CancellationToken cancellationToken = default)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(path) && !string.Equals(Path.GetFileName(path), DescriptionFileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        GameWorld? world;
        try
        {
            world = await ReadWorldAsync(folder, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }

        if (world is null || !SafePath.IsSafeSegment(world.WorldId))
        {
            return null;
        }

        IReadOnlyList<InspectionItem> summary =
        [
            new("World", world.WorldName),
            new("Preset", world.Subtitle),
            new("Files", world.FileCount.ToString(CultureInfo.InvariantCulture)),
            new("Size", DisplayFormatter.FormatBytes(world.SizeBytes))
        ];
        return new ImportCandidate(world, world.WorldId, null, world.LastModifiedAt, summary);
    }

    public string? GetGameBackupCopyPath(GameWorld world) => null;

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world)
    {
        return Directory.EnumerateFiles(world.SavePath, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "LOCK", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public string GetExpectedWorldPath(GameSaveRoot root, string worldId) =>
        SafePath.CombineUnderRoot(root.WorldsPath, worldId);

    public string? GetSaveFormatVersion(string worldPath) => WindrosePaths.FormatVersion(worldPath);

    public void RememberWorldState(string worldPath) => health.RememberWorldState(worldPath);

    public IReadOnlyList<SaveHealthNotice>? CheckHealth(GameWorld world) => health.Check(world);

    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) => health.MoveLeftoversAside(world);

    // The game finds a world by its folder name, which must equal "islandId" inside WorldDescription.json. A payload
    // whose island id differs would appear as a broken or duplicate world, so it is refused.
    public void ValidatePayload(string payloadRoot, string expectedWorldId)
    {
        var description = new FileInfo(Path.Combine(payloadRoot, DescriptionFileName));
        if (!description.Exists || description.Length > MaxDescriptionBytes)
        {
            throw new InvalidDataException("The backup does not contain a valid Windrose world (WorldDescription.json is missing). Nothing was changed.");
        }

        WorldDescriptionDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorldDescriptionDocument>(File.ReadAllText(description.FullName), JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The backup's WorldDescription.json could not be read. Nothing was changed.");
        }

        var islandId = document?.WorldDescription?.IslandId;
        if (!string.IsNullOrWhiteSpace(islandId) && !string.Equals(islandId, expectedWorldId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This backup belongs to a different Windrose world. Restore it as its own world instead. Nothing was changed.");
        }
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
