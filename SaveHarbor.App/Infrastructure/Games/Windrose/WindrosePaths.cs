using System.IO;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Navigation inside a Windrose profile, starting from a world folder:
// <profile>\RocksDB_v2\<version>\Worlds\<islandId>. See docs/plans/windrose/00-overview.md §1.1.
public static class WindrosePaths
{
    public const string WorldsFolderName = "Worlds";
    public const string PlayersFolderName = "Players";
    public const string AccountsFolderName = "Accounts";
    public const string GameBackupsFolderName = "RocksDB_v2_Backups";
    public const string LegacyRootName = "RocksDB";

    // Marker in the names of folders SaveHarbor stages next to a world; such folders are never worlds.
    public const string SaveHarborFolderMarker = ".saveharbor-";

    public static bool IsSaveHarborFolder(string path) =>
        Path.GetFileName(path).Contains(SaveHarborFolderMarker, StringComparison.OrdinalIgnoreCase);

    // The version folder ("0.10.0") above Worlds, or null when the path is not inside a Windrose profile layout.
    public static string? FormatVersion(string worldPath) => VersionDirectory(worldPath)?.Name;

    // <profile>\RocksDB_v2\<version>\Players or Accounts: this PC's own characters and account data.
    public static string? SiblingDatabase(string worldPath, string folderName) =>
        VersionDirectory(worldPath) is { } version ? Path.Combine(version.FullName, folderName) : null;

    // The game's own checkpoint backups of this world, which it may restore from on start.
    public static string? GameBackupFolder(string worldPath) =>
        ProfileDirectory(worldPath) is { } profile
            ? Path.Combine(profile.FullName, GameBackupsFolderName, WorldsFolderName, Path.GetFileName(worldPath))
            : null;

    // The pre-0.10.0.5.120 copy of this world, if the game still keeps one.
    public static string? LegacyWorldFolder(string worldPath) =>
        ProfileDirectory(worldPath) is { } profile && FormatVersion(worldPath) is { } version
            ? Path.Combine(profile.FullName, LegacyRootName, version, WorldsFolderName, Path.GetFileName(worldPath))
            : null;

    private static DirectoryInfo? VersionDirectory(string worldPath)
    {
        var worlds = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(worldPath)));
        return worlds is not null && string.Equals(worlds.Name, WorldsFolderName, StringComparison.OrdinalIgnoreCase)
            ? worlds.Parent
            : null;
    }

    private static DirectoryInfo? ProfileDirectory(string worldPath) => VersionDirectory(worldPath)?.Parent?.Parent;
}
