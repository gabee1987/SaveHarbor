using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Windrose loads a world from its own checkpoint archive, RocksDB_v2_Backups\Worlds\<id>\<id>_<version>_Latest.zip,
// and rebuilds the world folder from it whenever it loads the world. A world folder copied without that archive is not
// listed by the game (docs/plans/windrose/00-overview.md findings F8 and F10), so the archive travels with every
// SaveHarbor backup and the backed-up folder is rebuilt from it the same way.
public static partial class WindroseGameArchive
{
    private const long MaxArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const long MaxRebuiltBytes = 16L * 1024 * 1024 * 1024;
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

    // The folder differs from the archive after every session, because the game compacts the database on exit after
    // writing the archive, and the game will load the archive anyway. So the payload holds the archive and, in place of
    // the copied folder, the folder rebuilt from it: exactly the state the game loads, on this PC and any other. When
    // the archive is not a complete checkpoint, the copied folder is kept.
    public static void Stage(string worldPath, string payloadRoot)
    {
        if (LatestPath(worldPath) is not { } latest)
        {
            return;
        }

        var gameFilesRoot = Path.Combine(payloadRoot, BackupPayloadLayout.GameFilesFolderName);
        Directory.CreateDirectory(gameFilesRoot);
        var staged = Path.Combine(gameFilesRoot, Path.GetFileName(latest));
        File.Copy(latest, staged);

        var rebuilt = Directory.CreateTempSubdirectory("SaveHarbor-windrose-").FullName;
        try
        {
            bool complete;
            try
            {
                complete = TryRebuildWorldFolder(staged, rebuilt);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                complete = false;
            }

            if (!complete)
            {
                return;
            }

            foreach (var file in Directory.GetFiles(payloadRoot))
            {
                File.Delete(file);
            }

            foreach (var folder in Directory.GetDirectories(payloadRoot).Where(folder => !string.Equals(folder, gameFilesRoot, StringComparison.OrdinalIgnoreCase)))
            {
                Directory.Delete(folder, recursive: true);
            }

            foreach (var file in Directory.GetFiles(rebuilt))
            {
                File.Move(file, Path.Combine(payloadRoot, Path.GetFileName(file)));
            }
        }
        finally
        {
            Directory.Delete(rebuilt, recursive: true);
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

    // Writes the world folder the way the game restores it from the archive: tables and blobs under their plain file
    // numbers, the private files as they are, and the world description. False when the archive is not a complete
    // checkpoint in the known layout; every name is checked before anything is written, and only into targetFolder.
    private static bool TryRebuildWorldFolder(string archivePath, string targetFolder)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
        {
            return false;
        }

        var files = new List<(ZipArchiveEntry Entry, string Name)>();
        long totalBytes = 0;
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && !IsBookkeepingEntry(entry.FullName)))
        {
            totalBytes += entry.Length;
            if (RebuiltName(entry.FullName) is not { } name || totalBytes > MaxRebuiltBytes)
            {
                return false;
            }

            files.Add((entry, name));
        }

        var names = files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count != files.Count
            || !names.Contains("CURRENT")
            || !names.Contains(WindroseSaveAdapter.DescriptionFileName)
            || !names.Any(name => name.StartsWith("MANIFEST-", StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var (entry, name) in files)
        {
            entry.ExtractToFile(Path.Combine(targetFolder, name));
        }

        return true;
    }

    // The backup engine's own index, and a second copy of the world description that newer archives also carry there;
    // neither belongs in the world folder.
    private static bool IsBookkeepingEntry(string entryName) =>
        entryName.StartsWith("Checkpoint/meta/", StringComparison.Ordinal)
        || entryName.StartsWith("Checkpoint/AdditionalRecordFiles/", StringComparison.Ordinal);

    private static string? RebuiltName(string entryName)
    {
        if (entryName == DescriptionEntry)
        {
            return WindroseSaveAdapter.DescriptionFileName;
        }

        if (SharedTable().Match(entryName) is { Success: true } table)
        {
            return $"{table.Groups["n"].Value}.{table.Groups["ext"].Value}";
        }

        return PrivateFile().Match(entryName) is { Success: true } own ? own.Groups["name"].Value : null;
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

    [GeneratedRegex(@"^Checkpoint/private/\d{1,9}/(?<name>CURRENT|MANIFEST-\d{1,18}|OPTIONS-\d{1,18}|\d{1,18}\.log)$")]
    private static partial Regex PrivateFile();

    [GeneratedRegex(@"^Checkpoint/shared_checksum/(?<n>\d{1,18})_[A-Za-z0-9]{1,64}_\d{1,18}\.(?<ext>sst|blob)$")]
    private static partial Regex SharedTable();
}
