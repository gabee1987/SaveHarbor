using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Backup;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Problems with a Windrose world that SaveHarbor can see from outside the game: the game swapping the world for
// another copy after SaveHarbor wrote it, and folders an interrupted restore left where the game looks for worlds.
public sealed class WindroseSaveHealth(IAppDataPathProvider pathProvider)
{
    private readonly WindroseWorldLedger ledger = new(pathProvider);

    // Without a record the check simply has nothing to compare, so a failed write must not fail the restore that
    // already succeeded.
    public void RememberWorldState(string worldPath)
    {
        try
        {
            ledger.Remember(worldPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<SaveHealthNotice> Check(GameWorld world)
    {
        var notices = new List<SaveHealthNotice>();
        if (ledger.FindReplacement(world.SavePath) is { } writtenAt)
        {
            notices.Add(new(
                SaveHealthIssue.WorldReplaced,
                "The game replaced this world",
                $"SaveHarbor put this world in place on {writtenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}. "
                + "Since then its files were swapped for another copy, most likely Windrose's own backup. Check the world in the game before "
                + "uploading it. If progress is missing, restore the SaveHarbor backup; if the world is fine, keep it as it is."));
        }

        if (WindroseGameArchive.LatestPath(world.SavePath) is null)
        {
            notices.Add(new(
                SaveHealthIssue.GameArchiveMissing,
                "Windrose cannot load this world",
                "Windrose only lists worlds that also have its own archive in RocksDB_v2_Backups, and this world has none. "
                + "A copy made by SaveHarbor before version 2.2.1 lacks it: ask whoever shared the world to upload it again with "
                + "SaveHarbor 2.2.1 or later, then download it again."));
        }

        var leftovers = WindrosePaths.LeftoverFolders(world.SavePath).Count;
        if (leftovers > 0)
        {
            notices.Add(new(
                SaveHealthIssue.Leftovers,
                "Unfinished SaveHarbor copies",
                $"{leftovers} folder{(leftovers == 1 ? string.Empty : "s")} from an interrupted restore sit next to this world, where the game "
                + "may load them as a second world with the same id. Move them aside; nothing is deleted."));
        }

        return notices;
    }

    // Moves leftover folders out of the game's Worlds folder into SaveHarbor's data folder. Returns where they went.
    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) =>
        [.. WindrosePaths.LeftoverFolders(world.SavePath).Select(folder => MoveAside(folder, "leftovers"))];

    // After the world was removed: the game would rebuild it from its archives, so they go to SaveHarbor's data folder.
    // The latest archive is also inside the backup taken before the removal; the dated ones exist only here.
    public string? MoveGameArchivesAside(GameWorld world) =>
        WindrosePaths.GameBackupFolder(world.SavePath) is { } folder && Directory.Exists(folder) ? MoveAside(folder, "removed") : null;

    private string MoveAside(string folder, string purpose)
    {
        var destinationRoot = Path.Combine(pathProvider.AppDataRoot, purpose, GameId.Windrose.ToStorageKey());
        Directory.CreateDirectory(destinationRoot);
        var destination = Path.Combine(destinationRoot, $"{Path.GetFileName(folder)}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}");
        if (string.Equals(Path.GetPathRoot(folder), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(folder, destination);
        }
        else
        {
            DirectoryPayloadStrategy.CopyDirectory(folder, destination, CancellationToken.None);
            Directory.Delete(folder, recursive: true);
        }

        return destination;
    }
}
