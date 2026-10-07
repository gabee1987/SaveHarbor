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
    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world)
    {
        var destinationRoot = Path.Combine(pathProvider.AppDataRoot, "leftovers", GameId.Windrose.ToStorageKey());
        Directory.CreateDirectory(destinationRoot);

        var moved = new List<string>();
        foreach (var folder in WindrosePaths.LeftoverFolders(world.SavePath))
        {
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

            moved.Add(destination);
        }

        return moved;
    }
}
