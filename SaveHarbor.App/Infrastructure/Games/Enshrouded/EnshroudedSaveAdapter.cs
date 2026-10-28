using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// One world is one "world<N>.ksd" file, identified by the GUID in its header (N is only a local slot). Worlds are read
// from the game's Local folder and from every Steam Cloud folder, but SaveHarbor only ever writes to the Local one:
// Steam would sync any change it made in a Steam Cloud folder. Character, global and settings files are never touched.
public sealed class EnshroudedSaveAdapter(IEnshroudedSaveRootLocator locator) : IGameSaveAdapter
{
    private const long MaxImportBytes = 256L * 1024 * 1024;
    private const string LocalLabel = "Local";
    private const string SteamLabel = "Steam Cloud";
    private const string AsideFolderName = "saveharbor-aside";
    private const string WorldExtension = ".ksd";
    private const string ArchiveFileName = "world" + WorldExtension;

    private readonly record struct WorldLocation(string Label, bool IsReadOnly);

    public WorldPayloadKind PayloadKind => WorldPayloadKind.FileSet;

    public string SaveRootPath => locator.Locate().LocalRoot;

    // The Local folder is always listed, even before the game created it, because it is where downloads go.
    public Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default)
    {
        var roots = locator.Locate();
        List<GameSaveRoot> saveRoots = [DescribeRoot("local", roots.LocalRoot, LocalLabel, canReceiveWorlds: true)];
        saveRoots.AddRange(roots.SteamRoots.Select(steam => DescribeRoot($"steam-{steam.SteamUserId}", steam.WorldsPath, SteamLabel, canReceiveWorlds: false)));
        return Task.FromResult<IReadOnlyList<GameSaveRoot>>(saveRoots);
    }

    public Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var roots = locator.Locate();
        var folders = roots.SteamRoots
            .Select(steam => (Path: steam.WorldsPath, Location: new WorldLocation(SteamLabel, IsReadOnly: true)))
            .Prepend((Path: roots.LocalRoot, Location: new WorldLocation(LocalLabel, IsReadOnly: false)));

        var worlds = new List<GameWorld>();
        foreach (var folder in folders)
        {
            foreach (var file in EnshroudedSlotMap.EnumerateWorldFiles(folder.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadWorld(file, folder.Location) is { } world)
                {
                    worlds.Add(world);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<GameWorld>>(worlds
            .OrderByDescending(world => world.LastModifiedAt)
            .ThenBy(world => world.IsReadOnly)
            .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadWorld(savePath, Locate(savePath)));

    public Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        Task.FromResult(EnshroudedWorldInspector.Facts(world, Locate(world.SavePath).Label));

    public Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        EnshroudedWorldInspector.InspectAsync(world, Locate(world.SavePath).Label, cancellationToken);

    public string ImportFileFilter => "Enshrouded world (world*.ksd, world*.ksd.backup-*)|world*.ksd;world*.ksd.backup-*";

    // Accepts a world file or one of the game's rolling copies of it. Character files share the header layout, so the
    // file name is what keeps them out.
    public Task<ImportCandidate?> ReadImportCandidateAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaxImportBytes || !EnshroudedSlotMap.TryGetSlotOfWorldOrCopy(path, out var slot))
        {
            return Task.FromResult<ImportCandidate?>(null);
        }

        var header = EnshroudedSaveHeader.TryRead(path);
        if (header is null)
        {
            return Task.FromResult<ImportCandidate?>(null);
        }

        var world = CreateWorld(file, header, slot, $"Slot {slot.ToString(CultureInfo.InvariantCulture)}", isReadOnly: true);
        List<InspectionItem> summary =
        [
            new("World", world.WorldName),
            new("File", file.Name),
            new("Save counter", header.SaveCounter.ToString(CultureInfo.InvariantCulture)),
            new("Last saved", header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
        ];
        return Task.FromResult<ImportCandidate?>(new ImportCandidate(world, header.WorldId, header.SaveCounter, header.SavedAtUtc, summary));
    }

    public string? GetGameBackupCopyPath(GameWorld world) => EnshroudedSlotMap.GameCopies(world.SavePath).FirstOrDefault();

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world) => [world.SavePath];

    // The slot number means nothing on another PC, so backups carry the world without it.
    public string GetArchiveFileName(string worldPath) => ArchiveFileName;

    // The slot depends on what the folder holds right now, so it is resolved each time and never remembered.
    public string GetExpectedWorldPath(GameSaveRoot root, string worldId)
    {
        if (!root.CanReceiveWorlds)
        {
            throw new InvalidOperationException("Enshrouded worlds are placed in the Local folder only.");
        }

        if (!EnshroudedSaveHeader.IsWorldId(worldId))
        {
            throw new InvalidDataException("A name received from shared data is not a valid file name.");
        }

        return EnshroudedSlotMap.ResolveTargetPath(root.WorldsPath, worldId);
    }

    public string? GetSaveFormatVersion(string worldPath) =>
        EnshroudedSaveHeader.TryRead(worldPath)?.FormatVersion.ToString(CultureInfo.InvariantCulture);

    // The payload must be exactly one Enshrouded world file whose header GUID is the world it would become. Backups
    // made before the archive name was "world.ksd" carry "world<N>.ksd"; both are accepted.
    public void ValidatePayload(string payloadRoot, string expectedWorldId)
    {
        var files = Directory.GetFiles(payloadRoot);
        if (files.Length != 1 || !string.Equals(Path.GetExtension(files[0]), WorldExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The backup does not contain exactly one Enshrouded world file. Nothing was changed.");
        }

        if (new FileInfo(files[0]).Length > MaxImportBytes
            || EnshroudedSaveHeader.TryRead(files[0]) is not { } header
            || !string.Equals(header.WorldId, expectedWorldId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The backup does not contain this Enshrouded world. Nothing was changed.");
        }
    }

    public void StageGameFiles(GameWorld world, string payloadRoot)
    {
    }

    public void PlaceGameFiles(string gameFilesRoot, string worldPath)
    {
    }

    // After a removal the newest rolling copy was already kept with the backup (GetGameBackupCopyPath); the older ones
    // are moved out of the "backup" folder so the game cannot offer them again.
    public string? MoveGameFilesAside(GameWorld world)
    {
        var copies = EnshroudedSlotMap.GameCopies(world.SavePath);
        if (world.IsReadOnly || copies.Count == 0)
        {
            return null;
        }

        var aside = Path.Combine(
            Path.GetDirectoryName(world.SavePath)!,
            AsideFolderName,
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + Path.GetFileNameWithoutExtension(world.SavePath));
        Directory.CreateDirectory(aside);
        foreach (var copy in copies)
        {
            File.Move(copy, Path.Combine(aside, Path.GetFileName(copy)));
        }

        return aside;
    }

    public void RememberWorldState(string worldPath)
    {
    }

    public IReadOnlyList<SaveHealthNotice>? CheckHealth(GameWorld world) => null;

    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) => [];

    private static GameSaveRoot DescribeRoot(string rootId, string path, string label, bool canReceiveWorlds)
    {
        if (!Directory.Exists(path))
        {
            return new GameSaveRoot(GameId.Enshrouded, rootId, path, path, $"{label} · not created yet", DateTimeOffset.MinValue, canReceiveWorlds);
        }

        var worldCount = EnshroudedSlotMap.EnumerateWorldFiles(path).Count(file => EnshroudedSaveHeader.TryRead(file) is not null);
        var lastModified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToLocalTime();
        return new GameSaveRoot(GameId.Enshrouded, rootId, path, path, $"{label} · {worldCount} world(s)", lastModified, canReceiveWorlds);
    }

    private static GameWorld? ReadWorld(string path, WorldLocation location)
    {
        if (!EnshroudedSlotMap.TryGetSlot(path, out var slot) || EnshroudedSaveHeader.TryRead(path) is not { } header)
        {
            return null;
        }

        var subtitle = $"Slot {slot.ToString(CultureInfo.InvariantCulture)} · {location.Label}";
        return CreateWorld(new FileInfo(path), header, slot, subtitle, location.IsReadOnly);
    }

    private static GameWorld CreateWorld(FileInfo file, EnshroudedSaveHeader header, int slot, string subtitle, bool isReadOnly) =>
        new(
            GameId.Enshrouded,
            header.WorldId,
            EnshroudedWorldNameReader.TryRead(file.FullName) ?? $"Enshrouded world {slot.ToString(CultureInfo.InvariantCulture)}",
            subtitle,
            file.FullName,
            file.CreationTime,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
            file.Length,
            1,
            isReadOnly);

    // A file outside every known save folder (a picked import, a test folder) is treated as Local.
    private WorldLocation Locate(string savePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? string.Empty;
        return locator.Locate().SteamRoots.Any(steam => IsSameFolder(steam.WorldsPath, folder))
            ? new WorldLocation(SteamLabel, IsReadOnly: true)
            : new WorldLocation(LocalLabel, IsReadOnly: false);
    }

    private static bool IsSameFolder(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
}
