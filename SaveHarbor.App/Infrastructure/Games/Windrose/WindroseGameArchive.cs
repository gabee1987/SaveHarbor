using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Windrose loads a world from its own checkpoint archive, RocksDB_v2_Backups\Worlds\<id>\<id>_<version>_Latest.zip,
// and rebuilds the world folder from it on start. A world folder copied without that archive is not listed by the
// game (docs/plans/windrose/00-overview.md finding F8), so the archive travels with every SaveHarbor backup.
public static partial class WindroseGameArchive
{
    private const long MaxArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private const long MaxDescriptionBytes = 1024 * 1024;
    private const string LatestSuffix = "_Latest.zip";
    private const string DescriptionEntry = "AdditionalRecordFiles/WorldDescription.json";

    // The game's latest archive of this world on this PC, or null when it has none.
    public static string? LatestPath(string worldPath)
    {
        var folder = WindrosePaths.GameBackupFolder(worldPath);
        var version = WindrosePaths.FormatVersion(worldPath);
        if (folder is null || version is null)
        {
            return null;
        }

        var path = Path.Combine(folder, $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(worldPath))}_{version}{LatestSuffix}");
        return File.Exists(path) ? path : null;
    }

    public static void Stage(string worldPath, string gameFilesRoot)
    {
        if (LatestPath(worldPath) is { } latest)
        {
            Directory.CreateDirectory(gameFilesRoot);
            File.Copy(latest, Path.Combine(gameFilesRoot, Path.GetFileName(latest)));
        }
    }

    // The staged archive is untrusted: it must be one archive of this world holding only checkpoint files, so the
    // game never unpacks anything else or another world under this id. Throws InvalidDataException otherwise.
    public static void Validate(string gameFilesRoot, string worldId)
    {
        if (!Directory.Exists(gameFilesRoot))
        {
            return;
        }

        var files = Directory.GetFiles(gameFilesRoot, "*", SearchOption.AllDirectories);
        if (files.Length != 1 || Directory.GetDirectories(gameFilesRoot).Length > 0 || !IsArchiveName(Path.GetFileName(files[0]), worldId))
        {
            throw Invalid("The backup's Windrose world archive is not in the expected form.");
        }

        var info = new FileInfo(files[0]);
        if (info.Length > MaxArchiveBytes)
        {
            throw Invalid("The backup's Windrose world archive is too large.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(info.FullName);
            if (archive.Entries.Count > MaxEntries || !archive.Entries.All(entry => IsAllowedEntry(entry.FullName)))
            {
                throw Invalid("The backup's Windrose world archive contains unexpected files.");
            }

            var description = archive.GetEntry(DescriptionEntry);
            if (description is null || description.Length > MaxDescriptionBytes || !archive.Entries.Any(entry => entry.FullName.StartsWith("Checkpoint/meta/", StringComparison.Ordinal)))
            {
                throw Invalid("The backup's Windrose world archive is incomplete.");
            }

            using var reader = new StreamReader(description.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var islandId = document.RootElement.TryGetProperty("WorldDescription", out var world) && world.TryGetProperty("islandId", out var id)
                ? id.GetString()
                : null;
            if (!string.Equals(islandId, worldId, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("The backup's Windrose world archive belongs to a different world.");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            throw ex as InvalidDataException ?? Invalid("The backup's Windrose world archive could not be read.");
        }
    }

    // Puts the archive where the game looks for it, named for this PC's database version. The game's dated archives
    // of the world are left alone; the previous latest archive is part of the safety backup made before the restore.
    public static void Place(string gameFilesRoot, string worldPath)
    {
        var staged = Directory.Exists(gameFilesRoot) ? Directory.GetFiles(gameFilesRoot).SingleOrDefault() : null;
        var folder = WindrosePaths.GameBackupFolder(worldPath);
        var version = WindrosePaths.FormatVersion(worldPath);
        if (staged is null || folder is null || version is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(worldPath))}_{version}{LatestSuffix}");
        var temporary = target + ".saveharbor-tmp";
        File.Copy(staged, temporary, overwrite: true);

        // Dated now, so Steam Cloud (which syncs this folder) sees this PC's newest change and uploads it instead of
        // keeping an older cloud copy of the world (the same rule as for Dragonwilds saves).
        File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow);
        File.Move(temporary, target, overwrite: true);
    }

    // Whether the latest archive holds the same database as the world folder: the same manifest and the same tables.
    // Null when there is no archive. The game rebuilds the folder from the archive, so a differing archive wins.
    public static bool? MatchesWorldFolder(string worldPath)
    {
        if (LatestPath(worldPath) is not { } latest)
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(latest);
            var archived = archive.Entries.Select(entry => entry.FullName).Take(MaxEntries + 1).ToArray();
            var archivedManifests = archived.Select(name => PrivateManifest().Match(name)).Where(match => match.Success).Select(match => match.Groups["n"].Value).ToHashSet();
            var archivedTables = archived.Select(name => SharedTable().Match(name)).Where(match => match.Success).Select(match => match.Groups["n"].Value).ToHashSet();

            var folderFiles = Directory.EnumerateFiles(worldPath).Select(Path.GetFileName).ToArray();
            var folderManifests = folderFiles.Select(name => FolderManifest().Match(name!)).Where(match => match.Success).Select(match => match.Groups["n"].Value).ToHashSet();
            var folderTables = folderFiles.Select(name => FolderTable().Match(name!)).Where(match => match.Success).Select(match => match.Groups["n"].Value).ToHashSet();

            return archivedTables.SetEquals(folderTables) && archivedManifests.Overlaps(folderManifests);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsArchiveName(string name, string worldId) =>
        name.StartsWith(worldId + "_", StringComparison.OrdinalIgnoreCase)
        && ArchiveVersion().IsMatch(name[(worldId.Length + 1)..]);

    private static bool IsAllowedEntry(string name) =>
        (name.StartsWith("Checkpoint/", StringComparison.Ordinal) || name.StartsWith("AdditionalRecordFiles/", StringComparison.Ordinal))
        && !name.Contains('\\')
        && !name.Split('/').Any(part => part is "." or "..");

    private static InvalidDataException Invalid(string reason) => new($"{reason} Nothing was changed.");

    [GeneratedRegex(@"^\d{1,4}(?:\.\d{1,6}){0,3}_Latest\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchiveVersion();

    [GeneratedRegex(@"^Checkpoint/private/\d{1,9}/MANIFEST-(?<n>\d{1,18})$")]
    private static partial Regex PrivateManifest();

    [GeneratedRegex(@"^Checkpoint/shared_checksum/0*(?<n>\d{1,18})_[^/]*\.(?:sst|blob)$")]
    private static partial Regex SharedTable();

    [GeneratedRegex(@"^MANIFEST-(?<n>\d{1,18})$")]
    private static partial Regex FolderManifest();

    [GeneratedRegex(@"^0*(?<n>\d{1,18})\.(?:sst|blob)$", RegexOptions.IgnoreCase)]
    private static partial Regex FolderTable();
}
