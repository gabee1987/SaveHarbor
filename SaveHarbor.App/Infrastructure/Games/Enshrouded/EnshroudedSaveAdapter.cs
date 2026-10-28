using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// One world is one "world<N>.ksd" file, identified by the GUID in its header (N is only a slot). SaveHarbor manages one
// folder per PC: the game's Steam Cloud folder of the signed-in account (see EnshroudedSaveRootLocator). A world it
// places there is a normal Steam Cloud world for the game, and Steam backs it up like one; the owner verified that
// Steam takes a file written there with the game closed without a sync conflict. Character, global and settings files
// are never touched.
public sealed class EnshroudedSaveAdapter(IEnshroudedSaveRootLocator locator, IAppDataPathProvider pathProvider) : IGameSaveAdapter
{
    private const long MaxImportBytes = 256L * 1024 * 1024;
    private const string SteamCloudLabel = "Steam Cloud";
    private const string ChosenFolderLabel = "Save folder";
    private const string AsideFolderName = "saveharbor-aside";
    private const string WorldExtension = ".ksd";
    private const string ArchiveFileName = "world" + WorldExtension;

    private readonly EnshroudedWorldLedger ledger = new(pathProvider);

    public WorldPayloadKind PayloadKind => WorldPayloadKind.FileSet;

    public string SaveRootPath => locator.Locate()?.WorldsPath ?? string.Empty;

    public Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GameSaveRoot> roots = locator.Locate() is { } root ? [DescribeRoot(root)] : [];
        return Task.FromResult(roots);
    }

    public Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var worlds = new List<GameWorld>();
        if (locator.Locate() is { } root)
        {
            foreach (var file in EnshroudedSlotMap.EnumerateWorldFiles(root.WorldsPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadWorld(file) is { } world)
                {
                    worlds.Add(world);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<GameWorld>>(worlds
            .OrderByDescending(world => world.LastModifiedAt)
            .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadWorld(savePath));

    public Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        Task.FromResult(EnshroudedWorldInspector.Facts(world, LocationLabel()));

    public Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        EnshroudedWorldInspector.InspectAsync(world, LocationLabel(), cancellationToken);

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

        var world = CreateWorld(file, header, slot);
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
    // are moved out of the "backup" folder so the game cannot offer them again. Steam Cloud tracks only the files at
    // the top of the folder, so neither folder is synced.
    public string? MoveGameFilesAside(GameWorld world)
    {
        var copies = EnshroudedSlotMap.GameCopies(world.SavePath);
        if (copies.Count == 0)
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
        if (EnshroudedSaveHeader.TryRead(worldPath) is { } header)
        {
            ledger.Remember(header.WorldId, header.SaveCounter);
        }
    }

    // Both notices make the share after the game closes refuse and a manual upload ask first.
    // A second slot with the same GUID: the game lists the world twice, and which copy is played (or downloaded into)
    // is no longer clear. A save counter below the highest one this PC has seen: an older copy of the world is in place,
    // and sharing it would hand the group a world without the newer progress. "Keep world as it is" in the world info
    // accepts that copy.
    public IReadOnlyList<SaveHealthNotice>? CheckHealth(GameWorld world)
    {
        if (EnshroudedSaveHeader.TryRead(world.SavePath) is not { } header)
        {
            return [];
        }

        var notices = new List<SaveHealthNotice>();
        if (OtherSlotsOf(world.SavePath, header.WorldId) is { Count: > 0 } otherSlots)
        {
            notices.Add(new SaveHealthNotice(
                SaveHealthIssue.DuplicateWorld,
                "This world is in more than one slot",
                $"Slot {DisplaySlotOf(world.SavePath)} and slot {string.Join(", ", otherSlots)} hold the same world, so Enshrouded lists it "
                + "twice and SaveHarbor cannot tell which copy has your latest progress. Open the copies in the game, keep the newest one "
                + "and delete the others on the SAVEGAMES:STEAM tab."));
        }

        if (ledger.Observe(header.WorldId, header.SaveCounter) is { } newer)
        {
            notices.Add(new SaveHealthNotice(
                SaveHealthIssue.WorldReplaced,
                "An older copy of this world is in place",
                $"Its save counter is {header.SaveCounter.ToString(CultureInfo.InvariantCulture)}, but on this PC it already reached "
                + $"{newer.HighestSaveCounter.ToString(CultureInfo.InvariantCulture)} ({newer.SeenAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}). "
                + "Something put an earlier file back, most likely Steam Cloud. Check the world in the game before sharing it. If progress is "
                + "missing, download the shared version or restore a SaveHarbor backup; if the world is fine, keep it as it is."));
        }

        return notices;
    }

    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) => [];

    private static GameSaveRoot DescribeRoot(EnshroudedSaveRoot root)
    {
        var label = Label(root);
        if (!Directory.Exists(root.WorldsPath))
        {
            return new GameSaveRoot(GameId.Enshrouded, "default", root.WorldsPath, root.WorldsPath, $"{label} · not created yet", DateTimeOffset.MinValue);
        }

        var worldCount = EnshroudedSlotMap.EnumerateWorldFiles(root.WorldsPath).Count(file => EnshroudedSaveHeader.TryRead(file) is not null);
        var lastModified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(root.WorldsPath), TimeSpan.Zero).ToLocalTime();
        return new GameSaveRoot(GameId.Enshrouded, "default", root.WorldsPath, root.WorldsPath, $"{label} · {worldCount} world(s)", lastModified);
    }

    private static string Label(EnshroudedSaveRoot root) => root.IsSteamCloud ? SteamCloudLabel : ChosenFolderLabel;

    private string LocationLabel() => locator.Locate() is { } root ? Label(root) : SteamCloudLabel;

    // The display slots of the other world files in the same folder that carry this GUID.
    private static IReadOnlyList<string> OtherSlotsOf(string worldPath, string worldId) =>
        EnshroudedSlotMap.EnumerateWorldFiles(Path.GetDirectoryName(worldPath) ?? string.Empty)
            .Where(file => !string.Equals(Path.GetFullPath(file), Path.GetFullPath(worldPath), StringComparison.OrdinalIgnoreCase))
            .Where(file => string.Equals(EnshroudedSaveHeader.TryRead(file)?.WorldId, worldId, StringComparison.Ordinal))
            .Select(file => EnshroudedSlotMap.TryGetSlot(file, out var slot) ? slot : -1)
            .Order()
            .Select(EnshroudedSlotMap.DisplaySlot)
            .ToArray();

    private static string DisplaySlotOf(string worldPath) =>
        EnshroudedSlotMap.TryGetSlot(worldPath, out var slot) ? EnshroudedSlotMap.DisplaySlot(slot) : "?";

    private static GameWorld? ReadWorld(string path) =>
        EnshroudedSlotMap.TryGetSlot(path, out var slot) && EnshroudedSaveHeader.TryRead(path) is { } header
            ? CreateWorld(new FileInfo(path), header, slot)
            : null;

    private static GameWorld CreateWorld(FileInfo file, EnshroudedSaveHeader header, int slot) =>
        new(
            GameId.Enshrouded,
            header.WorldId,
            EnshroudedWorldNameReader.TryRead(file.FullName) ?? $"Enshrouded world {EnshroudedSlotMap.DisplaySlot(slot)}",
            $"Slot {EnshroudedSlotMap.DisplaySlot(slot)}",
            file.FullName,
            file.CreationTime,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
            file.Length,
            1);
}
