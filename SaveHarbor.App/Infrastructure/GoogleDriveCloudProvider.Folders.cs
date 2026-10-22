using System.IO;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class GoogleDriveCloudProvider
{
    private static async Task<DriveFile> GetAndValidateSharedRootFolderAsync(
        DriveService service,
        string sharedFolderId,
        CancellationToken cancellationToken)
    {
        var request = service.Files.Get(sharedFolderId);
        request.Fields = "id,name,mimeType,capabilities/canEdit";
        request.SupportsAllDrives = true;

        var folder = await request.ExecuteAsync(cancellationToken);
        if (!string.Equals(folder.MimeType, FolderMimeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Configured Google Drive shared folder ID does not point to a folder.");
        }

        if (folder.Capabilities?.CanEdit == false)
        {
            throw new InvalidOperationException($"Google Drive account does not have edit access to shared folder '{folder.Name}'.");
        }

        return folder;
    }

    private async Task<string?> FindFolderPathAsync(
        DriveService service,
        GameId game,
        IReadOnlyList<string> relativePath,
        bool createMissing,
        CancellationToken cancellationToken)
    {
        var currentParentId = options.GetSharedFolderId(game);
        foreach (var pathPart in relativePath)
        {
            var folderId = await FindFolderIdAsync(service, currentParentId, pathPart, cancellationToken);
            if (folderId is null)
            {
                if (!createMissing)
                {
                    return null;
                }

                folderId = await CreateFolderAsync(service, currentParentId, pathPart, cancellationToken);
            }

            currentParentId = folderId;
        }

        return currentParentId;
    }

    private async Task<string?> FindWorldFolderIdAsync(DriveService service, GameId game, string worldId, CancellationToken cancellationToken)
    {
        return await FindFolderPathAsync(service, game, ["worlds", worldId], createMissing: false, cancellationToken);
    }

    private static async Task<string> CreateFolderAsync(DriveService service, string parentId, string name, CancellationToken cancellationToken)
    {
        var file = new DriveFile
        {
            Name = name,
            MimeType = FolderMimeType,
            Parents = [parentId]
        };

        var request = service.Files.Create(file);
        request.Fields = "id";
        request.SupportsAllDrives = true;
        var created = await request.ExecuteAsync(cancellationToken);
        return created.Id;
    }

    private static async Task<string?> FindFolderIdAsync(DriveService service, string parentId, string name, CancellationToken cancellationToken)
    {
        return await FindFileIdAsync(service, parentId, name, FolderMimeType, cancellationToken);
    }

    private static async Task<string?> FindFileIdByNameAsync(DriveService service, string parentId, string name, CancellationToken cancellationToken)
    {
        return await FindFileIdAsync(service, parentId, name, null, cancellationToken);
    }

    private static async Task<string?> FindFileIdAsync(
        DriveService service,
        string parentId,
        string name,
        string? mimeType,
        CancellationToken cancellationToken)
    {
        var query = new StringBuilder()
            .Append('\'').Append(EscapeQueryValue(parentId)).Append("' in parents")
            .Append(" and name = '").Append(EscapeQueryValue(name)).Append('\'')
            .Append(" and trashed = false");

        if (mimeType is not null)
        {
            query.Append(" and mimeType = '").Append(EscapeQueryValue(mimeType)).Append('\'');
        }

        var request = service.Files.List();
        request.Q = query.ToString();
        request.Fields = "files(id,name)";
        request.PageSize = 1;
        request.Spaces = "drive";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var result = await request.ExecuteAsync(cancellationToken);
        return result.Files.FirstOrDefault()?.Id;
    }

    private static async Task<bool> HasAnyChildAsync(DriveService service, string parentId, CancellationToken cancellationToken)
    {
        var request = service.Files.List();
        request.Q = $"'{EscapeQueryValue(parentId)}' in parents and trashed = false";
        request.Fields = "files(id)";
        request.PageSize = 1;
        request.Spaces = "drive";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var result = await request.ExecuteAsync(cancellationToken);
        return result.Files is { Count: > 0 };
    }

    private static async Task<IReadOnlyList<DriveFile>> ListChildFoldersAsync(
        DriveService service,
        string parentId,
        CancellationToken cancellationToken)
    {
        var query = new StringBuilder()
            .Append('\'').Append(EscapeQueryValue(parentId)).Append("' in parents")
            .Append(" and mimeType = '").Append(FolderMimeType).Append('\'')
            .Append(" and trashed = false");

        var folders = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var request = service.Files.List();
            request.Q = query.ToString();
            request.Fields = "nextPageToken,files(id,name)";
            request.PageSize = 100;
            request.Spaces = "drive";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            request.PageToken = pageToken;

            var result = await request.ExecuteAsync(cancellationToken);
            if (result.Files is not null)
            {
                folders.AddRange(result.Files);
            }

            pageToken = result.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return folders;
    }

    private static string EscapeQueryValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }
}
