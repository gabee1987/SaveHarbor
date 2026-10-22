using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Storage used by the group, and archives that no version row points to (an upload interrupted between storing the
// archive and publishing it).
public sealed partial class SupabaseCloudProvider : ICloudStorageMaintenance
{
    // A friend's upload stores its archive a moment before publishing it; younger archives are never called unused.
    private static readonly TimeSpan UnusedFileMinimumAge = TimeSpan.FromHours(1);

    public async Task<CloudStorageUsage?> GetStorageUsageAsync(GameId game, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return null;
        }

        var rows = await rest.ListGroupArchivesAsync(groupId, cancellationToken);
        return new CloudStorageUsage(
            rows.Sum(row => Math.Max(row.ArchiveSizeBytes, 0)),
            options.FreePlanStorageBytes > 0 ? options.FreePlanStorageBytes : null);
    }

    public async Task<CloudUnusedFiles> FindUnusedFilesAsync(GameId game, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return CloudUnusedFiles.None;
        }

        var used = (await rest.ListGroupArchivesAsync(groupId, cancellationToken))
            .Select(row => row.ArchivePath)
            .ToHashSet(StringComparer.Ordinal);
        var gamePrefix = GamePrefix(groupId, game);
        var cutoff = time.GetUtcNow() - UnusedFileMinimumAge;
        var paths = new List<string>();
        long totalBytes = 0;

        foreach (var folder in (await storage.ListAsync(gamePrefix, cancellationToken)).Where(entry => entry.IsFolder))
        {
            if (!SupabaseMapping.IsValidWorldId(folder.Name))
            {
                continue;
            }

            var worldPrefix = $"{gamePrefix}{folder.Name}/";
            foreach (var file in (await storage.ListAsync(worldPrefix, cancellationToken)).Where(entry => !entry.IsFolder))
            {
                var path = worldPrefix + file.Name;
                if (!used.Contains(path) && file.CreatedAt is { } createdAt && createdAt < cutoff)
                {
                    paths.Add(path);
                    totalBytes += Math.Max(file.Metadata?.Size ?? 0, 0);
                }
            }
        }

        return new CloudUnusedFiles(paths, totalBytes);
    }

    // Deletes only paths inside this game's folder of the active group, whatever the caller passes.
    public async Task RemoveUnusedFilesAsync(GameId game, CloudUnusedFiles files, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return;
        }

        var gamePrefix = GamePrefix(groupId, game);
        var paths = files.Paths
            .Where(path => path.StartsWith(gamePrefix, StringComparison.Ordinal) && !path.Contains("..", StringComparison.Ordinal))
            .ToArray();
        if (paths.Length > 0)
        {
            await storage.DeleteAsync(paths, cancellationToken);
        }
    }

    private static string GamePrefix(string groupId, GameId game) => $"{groupId}/{game.ToStorageKey()}/";
}
