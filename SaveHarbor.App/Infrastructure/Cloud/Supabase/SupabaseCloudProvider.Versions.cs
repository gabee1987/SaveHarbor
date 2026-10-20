using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

public sealed partial class SupabaseCloudProvider
{
    private const int MaxWorldNameLength = 120;

    // The archive goes to storage first and becomes a version only when publish_world_version accepts it; an archive
    // whose version was refused is deleted again.
    public async Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        var world = request.World;
        var version = request.VersionMetadata;
        if (version.ArchiveSizeBytes > options.MaxArchiveBytes)
        {
            return new CloudUploadResult(false, null,
                $"This world's archive is {DisplayFormatter.FormatBytes(version.ArchiveSizeBytes)}; the group's cloud allows up to {DisplayFormatter.FormatBytes(options.MaxArchiveBytes)}.");
        }

        if (groups.GetActiveGroupId(world.Game) is not { } groupId)
        {
            return new CloudUploadResult(false, null, $"Choose a group for {world.Game} before uploading.");
        }

        if (!SupabaseMapping.IsValidWorldId(world.WorldId) || !SupabaseMapping.VersionIdPattern().IsMatch(version.VersionId))
        {
            return new CloudUploadResult(false, null,
                "This world's folder name has characters SaveHarbor Cloud cannot store. Use letters, digits, spaces, '_', '-', '.' and brackets.");
        }

        var gameKey = world.Game.ToStorageKey();
        var objectPath = SupabaseMapping.ObjectPath(groupId, gameKey, world.WorldId, version.VersionId + ".zip");
        await storage.UploadAsync(objectPath, request.ArchivePath, cancellationToken);

        VersionDto row;
        try
        {
            row = await rest.PublishVersionAsync(
                groupId,
                gameKey,
                world.WorldId,
                WorldNameFor(world),
                request.PreviousManifest?.LatestVersion?.VersionNumber ?? 0,
                new PublishVersionDto
                {
                    VersionId = version.VersionId,
                    ArchivePath = objectPath,
                    ArchiveSha256 = version.ArchiveSha256.ToLowerInvariant(),
                    ArchiveSizeBytes = version.ArchiveSizeBytes,
                    UploaderDevice = version.UploaderMachine,
                    SourceWorldModifiedAt = version.SourceWorldModifiedAtUtc == default ? null : version.SourceWorldModifiedAtUtc,
                    BasedOnVersionNumber = version.BasedOnVersionNumber,
                    BasedOnVersionId = version.BasedOnVersionId
                },
                cancellationToken);
        }
        catch (SupabaseException exception)
        {
            await DeleteArchivesQuietlyAsync([objectPath]);
            if (exception.Code == SupabaseErrorCodes.VersionConflict)
            {
                logger.Warning(AppLogKeyword.CloudUpload, "SaveHarbor Cloud refused version {VersionNumber} of world {WorldId}: a newer version exists", version.VersionNumber, world.WorldId);
                return new CloudUploadResult(false, null, SupabaseErrorMapper.Describe(exception).Message);
            }

            throw;
        }

        worldUuids[UuidKey(groupId, gameKey, world.WorldId)] = row.WorldUuid;
        var latest = SupabaseMapping.ToVersion(row, gameKey, world.WorldId);
        var manifest = new CloudWorldManifest
        {
            SchemaVersion = 2,
            Provider = SupabaseMapping.ProviderName,
            Game = gameKey,
            WorldId = world.WorldId,
            WorldName = WorldNameFor(world),
            LatestVersion = latest,
            Retention = request.PreviousManifest?.Retention ?? new CloudRetentionSettings { KeepLatestVersions = Capabilities.DefaultRetentionCount },
            UpdatedAtUtc = latest.UploadedAtUtc
        };

        logger.Information(AppLogKeyword.CloudUpload, "Published version {VersionNumber} of world {WorldId} to SaveHarbor Cloud", latest.VersionNumber, world.WorldId);
        return new CloudUploadResult(true, manifest, $"Uploaded v{latest.VersionNumber}.");
    }

    public async Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var world = request.World;
        if (groups.GetActiveGroupId(world.Game) is not { } groupId)
        {
            return new CloudDownloadResult(false, null, $"Choose a group for {world.Game} before downloading.");
        }

        if (!SupabaseMapping.IsValidWorldId(world.WorldId) || !SafePath.IsSafeSegment(request.Version.ArchiveFileName))
        {
            return new CloudDownloadResult(false, null, "The cloud version has an invalid name. Nothing was downloaded.");
        }

        var objectPath = SupabaseMapping.ObjectPath(groupId, world.Game.ToStorageKey(), world.WorldId, request.Version.ArchiveFileName);
        await storage.DownloadAsync(objectPath, request.TargetArchivePath, cancellationToken);
        return new CloudDownloadResult(true, request.TargetArchivePath, $"Downloaded v{request.Version.VersionNumber}.");
    }

    public async Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        (await ListOwnVersionsAsync(game, worldId, cancellationToken))
            .Select(row => new CloudStoredVersion(row.VersionId + ".zip"))
            .ToArray();

    // Deletes the archive first: a row without its archive would be offered for download; an archive without its row
    // is only unused space.
    public async Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        var versionId = archiveFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? archiveFileName[..^4] : archiveFileName;
        var row = (await ListOwnVersionsAsync(game, worldId, cancellationToken)).FirstOrDefault(candidate => candidate.VersionId == versionId);
        if (row is null)
        {
            return;
        }

        await storage.DeleteAsync([row.ArchivePath], cancellationToken);
        await rest.DeleteVersionRowAsync(row.Id, cancellationToken);
    }

    // There is no recycle bin: CloudSyncService keeps the latest version as a local backup before calling this.
    public async Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return new CloudRemovalResult(false, $"Choose a group for {game} first.");
        }

        if (await ResolveWorldUuidAsync(game, groupId, worldId, cancellationToken) is not { } worldUuid)
        {
            return new CloudRemovalResult(true, "The world was already gone from the cloud.");
        }

        var rows = await ListOwnVersionsAsync(game, worldId, cancellationToken);
        await storage.DeleteAsync(rows.Select(row => row.ArchivePath).ToArray(), cancellationToken);
        await rest.RemoveWorldAsync(worldUuid, cancellationToken);
        worldUuids.TryRemove(UuidKey(groupId, game.ToStorageKey(), worldId), out _);
        return new CloudRemovalResult(true, $"Its {rows.Count} shared version(s) were deleted permanently.");
    }

    // Version rows of a world whose archive lies where this world's archives belong; any other row is ignored, so a
    // bad row can never make the app delete or read another world's files.
    private async Task<IReadOnlyList<VersionDto>> ListOwnVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId
            || !SupabaseMapping.IsValidWorldId(worldId)
            || await ResolveWorldUuidAsync(game, groupId, worldId, cancellationToken) is not { } worldUuid)
        {
            return [];
        }

        var gameKey = game.ToStorageKey();
        return (await rest.ListVersionsAsync(worldUuid, cancellationToken))
            .Where(row => row.GroupId == groupId
                && SupabaseMapping.VersionIdPattern().IsMatch(row.VersionId)
                && row.ArchivePath == SupabaseMapping.ObjectPath(groupId, gameKey, worldId, row.VersionId + ".zip"))
            .ToArray();
    }

    private async Task DeleteArchivesQuietlyAsync(IReadOnlyList<string> objectPaths)
    {
        try
        {
            await storage.DeleteAsync(objectPaths, CancellationToken.None);
        }
        catch (SupabaseException exception)
        {
            logger.Warning(AppLogKeyword.CloudUpload, "An unused SaveHarbor Cloud archive could not be deleted: {Code}", exception.Code);
        }
    }

    private static string WorldNameFor(GameWorld world)
    {
        var name = string.IsNullOrWhiteSpace(world.WorldName) ? world.WorldId : world.WorldName.Trim();
        return name.Length > MaxWorldNameLength ? name[..MaxWorldNameLength].Trim() : name;
    }
}
