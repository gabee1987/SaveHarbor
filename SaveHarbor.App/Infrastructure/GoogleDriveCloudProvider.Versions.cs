using System.IO;
using System.Net;
using Google;
using Google.Apis.Drive.v3;
using SaveHarbor.App.Domain;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class GoogleDriveCloudProvider
{
    public async Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var versionsFolderId = await FindFolderPathAsync(service, game, ["worlds", worldId, "versions"], createMissing: false, cancellationToken);
        if (versionsFolderId is null)
        {
            return [];
        }

        var versions = new List<CloudStoredVersion>();
        string? pageToken = null;
        do
        {
            var request = service.Files.List();
            request.Q = $"'{EscapeQueryValue(versionsFolderId)}' in parents and mimeType = '{ZipMimeType}' and trashed = false";
            request.Fields = "nextPageToken,files(name)";
            request.PageSize = 100;
            request.Spaces = "drive";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            request.PageToken = pageToken;

            var result = await request.ExecuteAsync(cancellationToken);
            versions.AddRange((result.Files ?? []).Select(file => new CloudStoredVersion(file.Name)));
            pageToken = result.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return versions;
    }

    // Moved to the Drive trash, not deleted, so a removed version can still be recovered there for 30 days.
    public async Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var versionsFolderId = await FindFolderPathAsync(service, game, ["worlds", worldId, "versions"], createMissing: false, cancellationToken);
        if (versionsFolderId is null)
        {
            return;
        }

        foreach (var name in new[] { Path.ChangeExtension(archiveFileName, ".json"), archiveFileName })
        {
            var fileId = await FindFileIdByNameAsync(service, versionsFolderId, name, cancellationToken);
            if (fileId is not null)
            {
                await TrashFileAsync(service, fileId, cancellationToken);
            }
        }
    }

    // The whole world folder goes to the Drive trash in one step, so a world is either fully listed or fully removed.
    // Google Drive lets only the folder's owner trash it in a normal (non-shared-drive) folder.
    public async Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var worldFolderId = await FindWorldFolderIdAsync(service, game, worldId, cancellationToken);
        if (worldFolderId is null)
        {
            return new CloudRemovalResult(true, "The world was already gone from the cloud.");
        }

        try
        {
            await TrashFileAsync(service, worldFolderId, cancellationToken);
            return new CloudRemovalResult(true, "Moved to the Google Drive trash, where it stays recoverable for 30 days.");
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Google Drive refused to trash world {WorldId}: {Status}", worldId, exception.HttpStatusCode);
            return new CloudRemovalResult(
                false,
                "Google Drive did not allow this account to remove the world. Only the person who first shared it owns its folder and can remove it. Ask them to do it, or remove the world's folder in Google Drive yourself.");
        }
    }

    private static async Task TrashFileAsync(DriveService service, string fileId, CancellationToken cancellationToken)
    {
        var update = service.Files.Update(new DriveFile { Trashed = true }, fileId);
        update.SupportsAllDrives = true;
        await update.ExecuteAsync(cancellationToken);
    }
}
