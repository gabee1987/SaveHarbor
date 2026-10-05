using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Backup;

// Decides which backups the "Keep backups" setting may delete. Losing a world must stay impossible, so beyond the
// newest N backups of the game it never removes: the backup just made, the newest backup of every world, a recent
// safety backup (made automatically before a restore or import, or the imported file itself), or any file whose
// name it does not recognise.
public static class BackupRetention
{
    public static readonly TimeSpan SafetyBackupProtection = TimeSpan.FromDays(14);

    public static readonly IReadOnlySet<string> SafetyReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BackupReasons.PreRestore,
        BackupReasons.PreImport,
        BackupReasons.Imported
    };

    public static IReadOnlyList<BackupInfo> SelectForDeletion(IReadOnlyList<BackupInfo> backups, int keepCount, string justCreatedPath, DateTimeOffset now)
    {
        if (keepCount <= 0)
        {
            return [];
        }

        var newestFirst = backups.OrderByDescending(backup => backup.CreatedAt).ToArray();
        var seenWorlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletable = new List<BackupInfo>();
        for (var index = 0; index < newestFirst.Length; index++)
        {
            var backup = newestFirst[index];
            if (!BackupFileName.TryParse(backup.FileName, out var world, out var reason))
            {
                continue;
            }

            var isNewestOfWorld = seenWorlds.Add(world);
            var isProtected = isNewestOfWorld
                || string.Equals(Path.GetFullPath(backup.FilePath), Path.GetFullPath(justCreatedPath), StringComparison.OrdinalIgnoreCase)
                || (SafetyReasons.Contains(reason) && now - backup.CreatedAt < SafetyBackupProtection);
            if (index >= keepCount && !isProtected)
            {
                deletable.Add(backup);
            }
        }

        return deletable;
    }
}
