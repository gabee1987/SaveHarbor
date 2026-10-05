using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Migrations;

public sealed record MigrationReport(int MovedItems, IReadOnlyList<string> Errors);

public sealed class LegacyLayoutMigrator(IAppDataPathProvider paths, IAppLogger logger)
{
    private const string MarkerFileName = "migrations.json";
    private const string MarkerKey = "LegacyLayoutV1";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MigrationReport Run()
    {
        var markerPath = Path.Combine(paths.AppDataRoot, MarkerFileName);
        if (IsMarked(markerPath))
        {
            return new MigrationReport(0, []);
        }

        var moved = 0;
        var errors = new List<string>();

        moved += MoveFiles(paths.LegacyBackupRoot, paths.GetBackupRoot(GameId.Windrose), "*.zip", errors);
        moved += MoveFiles(paths.LegacySyncStateRoot, paths.GetSyncStateRoot(GameId.Windrose), "*.json", errors);
        moved += MoveLocalTestWorlds(errors);

        if (errors.Count == 0)
        {
            WriteMarker(markerPath, errors);
        }

        logger.Information(AppLogKeyword.App, "Legacy layout migration moved {MovedItems} item(s) with {ErrorCount} error(s)", moved, errors.Count);
        return new MigrationReport(moved, errors);
    }

    private int MoveFiles(string sourceRoot, string targetRoot, string pattern, List<string> errors)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return 0;
        }

        var moved = 0;
        try
        {
            foreach (var source in Directory.EnumerateFiles(sourceRoot, pattern, SearchOption.TopDirectoryOnly).ToArray())
            {
                try
                {
                    Directory.CreateDirectory(targetRoot);
                    var target = Path.Combine(targetRoot, Path.GetFileName(source));
                    if (File.Exists(target))
                    {
                        logger.Warning(AppLogKeyword.App, "Migration skipped {FileName}: the target already exists", Path.GetFileName(source));
                        continue;
                    }

                    File.Move(source, target);
                    moved++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(source)}: {exception.Message}");
                    logger.Warning(AppLogKeyword.App, "Migration could not move {FileName}: {Message}", Path.GetFileName(source), exception.Message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{sourceRoot}: {exception.Message}");
        }

        return moved;
    }

    private int MoveLocalTestWorlds(List<string> errors)
    {
        var source = Path.Combine(paths.LegacyLocalTestCloudRoot, "worlds");
        var targetRoot = paths.GetLocalTestCloudRoot(GameId.Windrose);
        var target = Path.Combine(targetRoot, "worlds");
        if (!Directory.Exists(source) || Directory.Exists(target))
        {
            return 0;
        }

        try
        {
            Directory.CreateDirectory(targetRoot);
            Directory.Move(source, target);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"worlds: {exception.Message}");
            logger.Warning(AppLogKeyword.App, "Migration could not move the local test cloud worlds: {Message}", exception.Message);
            return 0;
        }
    }

    private static bool IsMarked(string markerPath)
    {
        try
        {
            return File.Exists(markerPath) && File.ReadAllText(markerPath).Contains(MarkerKey, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void WriteMarker(string markerPath, List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(paths.AppDataRoot);
            var marker = new Dictionary<string, string> { [MarkerKey] = DateTimeOffset.UtcNow.ToString("O") };
            File.WriteAllText(markerPath, JsonSerializer.Serialize(marker, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{MarkerFileName}: {exception.Message}");
        }
    }
}
