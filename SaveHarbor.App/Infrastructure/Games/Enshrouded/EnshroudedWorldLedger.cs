using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// Remembers the highest save counter each world has had on this PC. The game raises the counter on every save, and a
// friend's PC continues from the counter of the file it was given, so a lower counter later means an older copy of the
// world was put in place: Steam Cloud bringing back an earlier file, a Steam conflict answered with the older side, or
// a copy made by hand. Without this check SaveHarbor would see only "the world changed" and share the older copy as
// new progress.
public sealed class EnshroudedWorldLedger(IAppDataPathProvider pathProvider)
{
    private const long MaxRecordBytes = 4096;

    public sealed record WorldRecord(uint HighestSaveCounter, DateTimeOffset SeenAtUtc);

    // SaveHarbor wrote this file, or the player accepted it as it is: its counter becomes the baseline, lower or not.
    public void Remember(string worldId, uint saveCounter) => Write(worldId, new WorldRecord(saveCounter, DateTimeOffset.UtcNow));

    // Records a higher counter as the new baseline. Returns the record the counter is below, or null when the counter
    // is not lower than anything seen before.
    public WorldRecord? Observe(string worldId, uint saveCounter)
    {
        var record = Read(worldId);
        if (record is not null && saveCounter < record.HighestSaveCounter)
        {
            return record;
        }

        if (record is null || saveCounter > record.HighestSaveCounter)
        {
            Write(worldId, new WorldRecord(saveCounter, DateTimeOffset.UtcNow));
        }

        return null;
    }

    private WorldRecord? Read(string worldId)
    {
        try
        {
            var info = new FileInfo(RecordPath(worldId));
            return info.Exists && info.Length <= MaxRecordBytes
                ? JsonSerializer.Deserialize<WorldRecord>(File.ReadAllText(info.FullName))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Without a record the check has nothing to compare, so a failed write never fails what called it.
    private void Write(string worldId, WorldRecord record)
    {
        try
        {
            var path = RecordPath(worldId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The world id comes from a header SaveHarbor already checked (32 lowercase hex characters), so it is a safe name.
    private string RecordPath(string worldId) =>
        Path.Combine(pathProvider.GetSyncStateRoot(GameId.Enshrouded), "save-counters", worldId + ".json");
}
