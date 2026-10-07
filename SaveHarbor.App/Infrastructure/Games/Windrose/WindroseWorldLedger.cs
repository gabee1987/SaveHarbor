using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Remembers which database files a world had when SaveHarbor last wrote it (or the user accepted it as it is).
// RocksDB never reuses a file number: playing only adds files with higher numbers and deletes old ones. A file at or
// below the highest remembered number that was not there before therefore means the folder was swapped for another
// copy, typically the game restoring its own backup over a world SaveHarbor had put there. A copy whose numbers are
// all higher cannot be told apart from normal play, so a missing warning is not proof that nothing was replaced.
public sealed partial class WindroseWorldLedger(IAppDataPathProvider pathProvider)
{
    private const long MaxRecordBytes = 4 * 1024 * 1024;
    private const int MaxFiles = 50_000;

    public void Remember(string worldPath)
    {
        if (!Directory.Exists(worldPath))
        {
            return;
        }

        var files = DatabaseFiles(worldPath).Take(MaxFiles).ToDictionary(file => file.Name, file => file.Length, StringComparer.Ordinal);
        var recordPath = RecordPath(worldPath);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        var temporaryPath = recordPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new WorldRecord(DateTimeOffset.UtcNow, files)));
        File.Move(temporaryPath, recordPath, overwrite: true);
    }

    // When the world was last written by SaveHarbor, if its folder has been replaced since; otherwise null.
    public DateTimeOffset? FindReplacement(string worldPath)
    {
        var record = Read(worldPath);
        if (record is null || record.Files.Count == 0 || !Directory.Exists(worldPath))
        {
            return null;
        }

        var highest = record.Files.Keys.Max(FileNumber);
        foreach (var file in DatabaseFiles(worldPath))
        {
            if (FileNumber(file.Name) > highest)
            {
                continue;
            }

            // Tables never change once written; logs and manifests may grow, so only their presence counts.
            var isTable = file.Name.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
            if (!record.Files.TryGetValue(file.Name, out var length) || (isTable && length != file.Length))
            {
                return record.WrittenAtUtc;
            }
        }

        return null;
    }

    private WorldRecord? Read(string worldPath)
    {
        var recordPath = RecordPath(worldPath);
        try
        {
            var info = new FileInfo(recordPath);
            if (!info.Exists || info.Length > MaxRecordBytes)
            {
                return null;
            }

            var record = JsonSerializer.Deserialize<WorldRecord>(File.ReadAllText(recordPath));
            return record?.Files is { Count: <= MaxFiles } files && files.Keys.All(name => DatabaseFileName().IsMatch(name)) ? record : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // One record per world folder; the name is a hash of the path, so nothing read from the save becomes a file name.
    private string RecordPath(string worldPath)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worldPath)).ToUpperInvariant();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(pathProvider.GetSyncStateRoot(GameId.Windrose), "world-files", name + ".json");
    }

    private static IEnumerable<FileInfo> DatabaseFiles(string worldPath) =>
        new DirectoryInfo(worldPath).EnumerateFiles().Where(file => DatabaseFileName().IsMatch(file.Name));

    private static long FileNumber(string name) =>
        long.Parse(DatabaseFileName().Match(name).Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?:(?<n>\d{1,18})\.(?:sst|log|blob)|(?:MANIFEST|OPTIONS)-(?<n>\d{1,18}))$", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseFileName();

    private sealed record WorldRecord(DateTimeOffset WrittenAtUtc, Dictionary<string, long> Files);
}
