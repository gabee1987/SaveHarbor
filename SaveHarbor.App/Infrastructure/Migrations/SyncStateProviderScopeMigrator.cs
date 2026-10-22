using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Migrations;

// Sync state written before providers could be chosen sits directly in sync-state\<game>\ and counts Google Drive
// versions (or local test versions on a developer PC). Moves it into that provider's folder. Runs at every start, only
// touches *.json files directly in the game folder, and never overwrites or deletes anything.
public sealed class SyncStateProviderScopeMigrator(IAppDataPathProvider paths, CloudProviderOptions options, IAppLogger logger)
{
    public MigrationReport Run(IEnumerable<GameId> games)
    {
        var providerKey = CloudProviderKind.ToStorageKey(options.ForcesLocalTest ? CloudProviderKind.LocalTest : CloudProviderKind.GoogleDrive);
        var moved = 0;
        var errors = new List<string>();
        foreach (var game in games)
        {
            var root = paths.GetSyncStateRoot(game);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var targetRoot = paths.GetSyncStateRoot(game, providerKey);
            foreach (var source in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).ToArray())
            {
                var fileName = Path.GetFileName(source);
                try
                {
                    var target = Path.Combine(targetRoot, fileName);
                    if (File.Exists(target))
                    {
                        logger.Warning(AppLogKeyword.App, "Sync state {FileName} was not moved: the provider folder already has it", fileName);
                        continue;
                    }

                    Directory.CreateDirectory(targetRoot);
                    File.Move(source, target);
                    moved++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{fileName}: {exception.Message}");
                    logger.Warning(AppLogKeyword.App, "Sync state {FileName} could not be moved: {Message}", fileName, exception.Message);
                }
            }
        }

        if (moved > 0 || errors.Count > 0)
        {
            logger.Information(AppLogKeyword.App, "Moved {MovedItems} sync state file(s) into the {Provider} folder with {ErrorCount} error(s)", moved, providerKey, errors.Count);
        }

        return new MigrationReport(moved, errors);
    }
}
