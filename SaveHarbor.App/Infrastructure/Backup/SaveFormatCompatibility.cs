using System.Globalization;

namespace SaveHarbor.App.Infrastructure.Backup;

// A world saved by a newer game version can break an older game that loads it, so it is never put back there.
// Older or equal versions, and unknown versions (older backups recorded none), are allowed: the game upgrades them.
public static class SaveFormatCompatibility
{
    public static void EnsureCanRestore(string? backupVersion, string? targetVersion)
    {
        if (Version.TryParse(backupVersion, out var incoming)
            && Version.TryParse(targetVersion, out var installed)
            && incoming > installed)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "This world was saved by a newer game version (save format {0}; this PC has {1}). Update the game on this PC first. Nothing was changed.",
                backupVersion,
                targetVersion));
        }
    }
}
