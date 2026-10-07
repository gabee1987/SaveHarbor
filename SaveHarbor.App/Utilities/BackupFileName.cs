using System.Globalization;
using System.Text.RegularExpressions;

namespace SaveHarbor.App.Utilities;

// Backup archives are named "<yyyyMMdd_HHmmss UTC>_<safe world name>_<reason>[-n].zip". The "-n" counter is only
// added when a name is already taken, so an existing backup is never overwritten. Each world's backups sit in their
// own folder under the game's backup root, named after the world.
public static partial class BackupFileName
{
    public const string Extension = ".zip";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private const string FallbackFolderName = "_unnamed-world";

    public static string FolderName(string worldName)
    {
        var safeName = FileNameSanitizer.MakeSafeFileName(worldName).TrimEnd('.', ' ');
        return SafePath.IsSafeSegment(safeName) ? safeName : FallbackFolderName;
    }

    public static string Create(DateTimeOffset createdAt, string worldName, string reason, int attempt = 1) =>
        $"{createdAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{FileNameSanitizer.MakeSafeFileName(worldName)}_{reason}"
        + (attempt > 1 ? $"-{attempt}" : string.Empty)
        + Extension;

    public static bool TryParse(string fileName, out string safeWorldName, out string reason)
    {
        safeWorldName = string.Empty;
        reason = string.Empty;
        var stem = fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^Extension.Length] : fileName;
        var reasonSeparator = stem.LastIndexOf('_');
        if (stem.Length <= TimestampFormat.Length + 1
            || stem[TimestampFormat.Length] != '_'
            || !DateTime.TryParseExact(stem[..TimestampFormat.Length], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || reasonSeparator <= TimestampFormat.Length + 1)
        {
            return false;
        }

        safeWorldName = stem[(TimestampFormat.Length + 1)..reasonSeparator];
        reason = AttemptSuffix().Replace(stem[(reasonSeparator + 1)..], string.Empty);
        return safeWorldName.Length > 0 && reason.Length > 0;
    }

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex AttemptSuffix();
}
