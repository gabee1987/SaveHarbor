using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// Everything SaveHarbor can tell about a Windrose world for the "All world info" window. Character and account
// databases are only counted, never read: they are personal and never shared.
public static class WindroseWorldInspector
{
    public static async Task<IReadOnlyList<InspectionSection>> InspectAsync(GameWorld world, CancellationToken cancellationToken)
    {
        var settings = await WindroseWorldSettings.ReadAsync(Path.Combine(world.SavePath, WindroseSaveAdapter.DescriptionFileName), cancellationToken);
        var version = WindrosePaths.FormatVersion(world.SavePath);

        return
        [
            new("World",
            [
                new("World name", world.WorldName),
                new("World ID (island)", world.WorldId),
                new("Preset", world.Subtitle),
                new("Created", world.CreatedAt == DateTimeOffset.MinValue ? "Unknown" : world.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                new("Save format version", version ?? "Unknown")
            ]),
            new("World settings", DescribeSettings(settings)),
            new("Save folder", DescribeFolder(world)),
            new("Game's own copies", DescribeGameCopies(world)),
            new("This PC only (never shared)", DescribeLocalOnly(world))
        ];
    }

    private static IReadOnlyList<InspectionItem> DescribeSettings(WindroseWorldSettings settings)
    {
        var items = new List<InspectionItem>();
        foreach (var (tag, choice) in settings.Choices.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            items.Add(new(Label(tag), WindroseWorldFacts.LastSegment(choice)));
        }

        foreach (var (tag, flag) in settings.Flags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            items.Add(new(WindroseWorldFacts.FlagLabels.GetValueOrDefault(tag) ?? Label(tag), flag ? "On" : "Off"));
        }

        foreach (var (tag, value) in settings.Multipliers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            items.Add(new(WindroseWorldFacts.MultiplierLabels.GetValueOrDefault(tag) ?? Label(tag), WindroseWorldFacts.FormatMultiplier(value)));
        }

        return items.Count > 0 ? items : [new("Settings", "None recorded (preset defaults)")];
    }

    private static string Label(string tag) => tag == WindroseWorldFacts.CombatDifficultyTag ? "Combat" : WindroseWorldSettings.ShortName(tag);

    private static IReadOnlyList<InspectionItem> DescribeFolder(GameWorld world)
    {
        var files = Directory.Exists(world.SavePath)
            ? new DirectoryInfo(world.SavePath).EnumerateFiles("*", SearchOption.AllDirectories).ToArray()
            : [];
        int Count(Func<FileInfo, bool> match) => files.Count(match);

        return
        [
            new("Folder", world.SavePath),
            new("Files", files.Length.ToString(CultureInfo.InvariantCulture)),
            new("Size", DisplayFormatter.FormatBytes(files.Sum(file => file.Length))),
            new("Last changed", world.LastModifiedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("Data tables (.sst)", Count(file => file.Extension.Equals(".sst", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture)),
            new("Write-ahead logs (.log)", Count(file => file.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture)),
            new("Database index files", Count(file => file.Name.StartsWith("MANIFEST-", StringComparison.Ordinal) || file.Name is "CURRENT" or "IDENTITY").ToString(CultureInfo.InvariantCulture)),
            new("Open by the game (LOCK file)", files.Any(file => file.Name == "LOCK")
                ? "Yes — the game is running or did not close cleanly. SaveHarbor never copies the LOCK file."
                : "No")
        ];
    }

    private static IReadOnlyList<InspectionItem> DescribeGameCopies(GameWorld world)
    {
        var items = new List<InspectionItem>();
        var backupFolder = WindrosePaths.GameBackupFolder(world.SavePath);
        if (backupFolder is not null && Directory.Exists(backupFolder))
        {
            var archives = new DirectoryInfo(backupFolder).GetFiles("*.zip");
            items.Add(new("Game backup folder", backupFolder));
            items.Add(new("Game backups", archives.Length.ToString(CultureInfo.InvariantCulture)));
            if (archives.Length > 0)
            {
                items.Add(new("Newest game backup", archives.Max(file => file.LastWriteTime).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            }

            items.Add(new("Note", "Windrose can restore this world from these backups when it starts. Steam Cloud synchronises this folder."));
        }
        else
        {
            items.Add(new("Game backups", "None for this world"));
        }

        var legacy = WindrosePaths.LegacyWorldFolder(world.SavePath);
        items.Add(new("Pre-0.10.0.5 copy", legacy is not null && Directory.Exists(legacy) ? legacy : "None"));
        return items;
    }

    private static IReadOnlyList<InspectionItem> DescribeLocalOnly(GameWorld world)
    {
        static string CountFolders(string? path) =>
            path is not null && Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).Count().ToString(CultureInfo.InvariantCulture)
                : "0";

        return
        [
            new("Characters on this PC", CountFolders(WindrosePaths.SiblingDatabase(world.SavePath, WindrosePaths.PlayersFolderName))),
            new("Accounts on this PC", CountFolders(WindrosePaths.SiblingDatabase(world.SavePath, WindrosePaths.AccountsFolderName))),
            new("Note", "Characters belong to each player and stay on their own PC. When you join a friend's world you bring your own character; SaveHarbor shares only the world.")
        ];
    }
}
