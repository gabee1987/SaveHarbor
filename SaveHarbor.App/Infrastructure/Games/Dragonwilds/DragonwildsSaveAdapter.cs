using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public sealed class DragonwildsSaveAdapter(GameOptionsProvider optionsProvider) : IGameSaveAdapter
{
    private const string WorldExtension = ".sav";
    private static readonly string[] BackupMarkers = [".bak", ".backup"];

    public WorldPayloadKind PayloadKind => WorldPayloadKind.FileSet;

    public string SaveRootPath => ResolveRoot();

    public async Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var root = ResolveRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        var worlds = new List<GameWorld>();
        foreach (var file in EnumerateWorldFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var world = await ReadWorldAsync(file, cancellationToken);
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
        var root = ResolveRoot();
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<GameSaveRoot>>([]);
        }

        var worldCount = EnumerateWorldFiles(root).Count();
        var lastModified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(root), TimeSpan.Zero).ToLocalTime();
        GameSaveRoot saveRoot = new(GameId.Dragonwilds, "steam", root, root, $"{worldCount} world(s)", lastModified);
        return Task.FromResult<IReadOnlyList<GameSaveRoot>>([saveRoot]);
    }

    public Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default)
    {
        if (!IsWorldFileName(savePath) || !File.Exists(savePath))
        {
            return Task.FromResult<GameWorld?>(null);
        }

        var stem = Path.GetFileNameWithoutExtension(savePath);
        var worldId = FileNameSanitizer.MakeSafeFileName(stem);
        if (!SafePath.IsSafeSegment(worldId))
        {
            return Task.FromResult<GameWorld?>(null);
        }

        var file = new FileInfo(savePath);
        GameWorld world = new(
            GameId.Dragonwilds,
            worldId,
            stem,
            string.Empty,
            file.FullName,
            file.CreationTime,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
            file.Length,
            1);
        return Task.FromResult<GameWorld?>(world);
    }

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world) => [world.SavePath];

    public string GetExpectedWorldPath(GameSaveRoot root, string worldId)
    {
        if (!SafePath.IsSafeSegment(worldId))
        {
            throw new InvalidDataException("A name received from shared data is not a valid file name.");
        }

        return SafePath.CombineUnderRoot(root.WorldsPath, worldId + WorldExtension);
    }

    private static IEnumerable<string> EnumerateWorldFiles(string root)
    {
        // "*.sav" is not used as a search pattern: a three-character extension pattern also matches ".save" on Windows.
        return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).Where(IsWorldFileName);
    }

    private static bool IsWorldFileName(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(Path.GetExtension(name), WorldExtension, StringComparison.OrdinalIgnoreCase)
            && !BackupMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveRoot()
    {
        var overrideRoot = optionsProvider.Get(GameId.Dragonwilds).SaveRoot;
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Environment.ExpandEnvironmentVariables(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "RSDragonwilds", "Saved", "SaveGames");
    }
}
