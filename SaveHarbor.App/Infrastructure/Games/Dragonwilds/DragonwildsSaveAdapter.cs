using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public sealed class DragonwildsSaveAdapter(GameOptionsProvider optionsProvider) : IGameSaveAdapter
{
    private const string WorldExtension = ".sav";
    private const long MaxImportBytes = 1024L * 1024 * 1024;
    private static readonly string[] BackupMarkers = [".bak", ".backup"];
    private static readonly string[] NonWorldFileStems = ["EnhancedInputUserSettings"];

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

    public async Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        var info = await DragonwildsSaveInfoReader.TryReadAsync(world.SavePath, cancellationToken);
        return info is null ? [] : DragonwildsWorldFacts.From(info);
    }

    public Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        DragonwildsWorldInspector.InspectAsync(world.SavePath, cancellationToken);

    public string ImportFileFilter => "Dragonwilds world save (*.sav, *.sav.backup)|*.sav;*.sav.backup";

    public async Task<ImportCandidate?> ReadImportCandidateAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        var name = file.Name;
        if (name.EndsWith(DragonwildsWorldInspector.GameBackupSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^DragonwildsWorldInspector.GameBackupSuffix.Length];
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        if (!file.Exists
            || file.Length > MaxImportBytes
            || !string.Equals(Path.GetExtension(name), WorldExtension, StringComparison.OrdinalIgnoreCase)
            || NonWorldFileStems.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var info = await DragonwildsSaveInfoReader.TryReadAsync(file.FullName, cancellationToken);
        var worldId = ChooseImportWorldId(stem, info?.WorldName);
        if (info is null || worldId is null)
        {
            return null;
        }

        GameWorld world = new(
            GameId.Dragonwilds,
            worldId,
            string.IsNullOrWhiteSpace(info.WorldName) ? worldId : info.WorldName,
            string.Empty,
            file.FullName,
            file.CreationTime,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
            file.Length,
            1);

        List<InspectionItem> summary = [new("World", world.WorldName), new("File", file.Name)];
        summary.AddRange(DragonwildsWorldFacts.From(info)
            .Where(fact => fact.Label is "Created by" or "Last saved" or "Game build")
            .Select(fact => new InspectionItem(fact.Label, fact.Value)));
        if (info.SaveRevision is { } revision)
        {
            summary.Add(new InspectionItem("Save revision", revision.ToString(CultureInfo.InvariantCulture)));
        }

        return new ImportCandidate(world, info.WorldGuid, info.SaveRevision, info.SavedAtUtc, summary);
    }

    // Browsers and Explorer rename duplicates ("Crystalwind (1).sav", "Crystalwind - Copy.sav"). When the name stored
    // inside the save shows that happened, the world is imported under its own name; otherwise the file name is kept.
    private static string? ChooseImportWorldId(string stem, string? storedName)
    {
        if (!string.IsNullOrWhiteSpace(storedName)
            && SafePath.IsSafeSegment(storedName)
            && FileNameSanitizer.MakeSafeFileName(storedName) == storedName
            && Regex.IsMatch(stem, $@"^{Regex.Escape(storedName)}( \(\d+\)| - Copy( \(\d+\))?)$", RegexOptions.IgnoreCase))
        {
            return storedName;
        }

        var worldId = FileNameSanitizer.MakeSafeFileName(stem);
        return SafePath.IsSafeSegment(worldId) && worldId == stem ? worldId : null;
    }

    public string? GetGameBackupCopyPath(GameWorld world)
    {
        var path = world.SavePath + DragonwildsWorldInspector.GameBackupSuffix;
        return File.Exists(path) ? path : null;
    }

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world) => [world.SavePath];

    // Dragonwilds saves carry no format folder; the file-set strategy already checks names and hashes.
    public string? GetSaveFormatVersion(string worldPath) => null;

    public void ValidatePayload(string payloadRoot, string expectedWorldId)
    {
    }

    public void RememberWorldState(string worldPath)
    {
    }

    public IReadOnlyList<SaveHealthNotice>? CheckHealth(GameWorld world) => null;

    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) => [];

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
            && !BackupMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            && !NonWorldFileStems.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase);
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
