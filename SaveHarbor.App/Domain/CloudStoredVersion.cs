using System.Text.RegularExpressions;

namespace SaveHarbor.App.Domain;

// A version archive found in a world's cloud "versions" folder. The number comes from the archive name
// ("<timestamp>_<player>_v<n>.zip", see CloudSyncService); archives without one are never removed.
public sealed partial record CloudStoredVersion(string ArchiveFileName)
{
    public int? VersionNumber =>
        VersionSuffix().Match(ArchiveFileName) is { Success: true } match && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : null;

    [GeneratedRegex(@"_v(\d{1,9})\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();
}

// Decides which shared versions the "Keep shared versions" setting may remove after an upload. Always kept: the
// newest N, the latest version, any version an active session is based on, and archives it cannot number.
public static class CloudVersionRetention
{
    public static IReadOnlyList<CloudStoredVersion> SelectForRemoval(
        IReadOnlyList<CloudStoredVersion> stored,
        int keepCount,
        int latestVersionNumber,
        IReadOnlySet<int> protectedVersionNumbers)
    {
        if (keepCount <= 0)
        {
            return [];
        }

        return stored
            .Where(version => version.VersionNumber is not null)
            .OrderByDescending(version => version.VersionNumber)
            .Skip(keepCount)
            .Where(version => version.VersionNumber < latestVersionNumber && !protectedVersionNumbers.Contains(version.VersionNumber!.Value))
            .ToArray();
    }
}
