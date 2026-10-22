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
    private static async Task<T?> DownloadJsonByNameAsync<T>(
        DriveService service,
        string parentId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var fileId = await FindFileIdByNameAsync(service, parentId, fileName, cancellationToken);
        if (fileId is null)
        {
            return default;
        }

        await using var stream = new MemoryStream();
        var downloadRequest = service.Files.Get(fileId);
        downloadRequest.SupportsAllDrives = true;
        var download = await downloadRequest.DownloadAsync(stream, cancellationToken);
        if (download.Status != DownloadStatus.Completed)
        {
            throw new IOException($"Google Drive JSON download failed: {download.Exception?.Message ?? download.Status.ToString()}");
        }

        stream.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task UploadJsonByNameAsync<T>(
        DriveService service,
        string parentId,
        string fileName,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await UploadStreamByNameAsync(service, parentId, fileName, stream, JsonMimeType, cancellationToken);
    }

    private static async Task UploadFileByNameAsync(
        DriveService service,
        string parentId,
        string fileName,
        string sourcePath,
        string mimeType,
        CancellationToken cancellationToken)
    {
        await using var stream = System.IO.File.OpenRead(sourcePath);
        await UploadStreamByNameAsync(service, parentId, fileName, stream, mimeType, cancellationToken);
    }

    private static async Task UploadStreamByNameAsync(
        DriveService service,
        string parentId,
        string fileName,
        Stream stream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var existingId = await FindFileIdByNameAsync(service, parentId, fileName, cancellationToken);
        IUploadProgress upload;

        if (existingId is null)
        {
            var metadata = new DriveFile
            {
                Name = fileName,
                Parents = [parentId]
            };

            var create = service.Files.Create(metadata, stream, mimeType);
            create.Fields = "id";
            create.SupportsAllDrives = true;
            upload = await create.UploadAsync(cancellationToken);
        }
        else
        {
            var metadata = new DriveFile { Name = fileName };
            var update = service.Files.Update(metadata, existingId, stream, mimeType);
            update.Fields = "id";
            update.SupportsAllDrives = true;
            upload = await update.UploadAsync(cancellationToken);
        }

        if (upload.Status != UploadStatus.Completed)
        {
            throw new IOException($"Google Drive upload failed: {upload.Exception?.Message ?? upload.Status.ToString()}");
        }
    }
}
