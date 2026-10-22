using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Migrations;

// Moves backups made before each world had its own backup folder into that folder. Runs at every start and only
// touches recognised backup names directly in a game's backup root; it never overwrites or deletes anything.
public sealed class BackupFolderOrganizer(IAppDataPathProvider paths, IAppLogger logger)
{
    public MigrationReport Run(IEnumerable<GameId> games)
    {
        var moved = 0;
        var errors = new List<string>();
        foreach (var game in games)
        {
            var root = paths.GetBackupRoot(game);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var source in Directory.EnumerateFiles(root, "*" + BackupFileName.Extension, SearchOption.TopDirectoryOnly).ToArray())
            {
                var fileName = Path.GetFileName(source);
                if (!BackupFileName.TryParse(fileName, out var safeWorldName, out _))
                {
                    continue;
                }

                try
                {
                    var folder = Path.Combine(root, BackupFileName.FolderName(safeWorldName));
                    var target = Path.Combine(folder, fileName);
                    if (File.Exists(target))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(folder);
                    File.Move(source, target);
                    moved++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{fileName}: {exception.Message}");
                    logger.Warning(AppLogKeyword.App, "Could not move backup {FileName} into its world folder: {Message}", fileName, exception.Message);
                }
            }
        }

        if (moved > 0)
        {
            logger.Information(AppLogKeyword.App, "Moved {MovedItems} backup(s) into per-world folders", moved);
        }

        return new MigrationReport(moved, errors);
    }
}
