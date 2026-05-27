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

public sealed class GoogleDriveCloudProvider : ICloudProvider, ISharedFolderCloudProvider
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string JsonMimeType = "application/json";
    private const string ZipMimeType = "application/zip";

    private static readonly string[] Scopes = [DriveService.Scope.Drive];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAppDataPathProvider pathProvider;
    private readonly CloudProviderOptions options;
    private readonly IAppLogger logger;
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    private DriveService? driveService;
    private string? accountEmail;

    public GoogleDriveCloudProvider(
        IAppDataPathProvider pathProvider,
        CloudProviderOptions options,
        IAppLogger logger)
    {
        this.pathProvider = pathProvider;
        this.options = options;
        this.logger = logger;
    }

    public string ProviderName => "Google Drive";

    public async Task<CloudConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!HasClientSecrets(out var secretsPath))
        {
            return new CloudConnectionStatus(
                false,
                ProviderName,
                null,
                $"Google Drive is not configured. Put OAuth client secrets at {secretsPath} or set SaveHarbor:CloudProvider:GoogleClientSecretsPath.");
        }

        if (!HasSharedFolder(out var sharedFolderMessage))
        {
            return new CloudConnectionStatus(false, ProviderName, null, sharedFolderMessage);
        }

        if (driveService is null)
        {
            try
            {
                await GetOrCreateServiceAsync(interactive: false, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.Warning(AppLogKeyword.CloudProvider, "Silent Google Drive reconnect failed: {Message}", exception.Message);
                return new CloudConnectionStatus(
                    false,
                    ProviderName,
                    null,
                    "Google Drive is configured but not connected. Use Connect before checking, uploading, or downloading.");
            }
        }

        var service = driveService ?? throw new InvalidOperationException("Google Drive service was not created.");
        accountEmail ??= await LoadAccountEmailAsync(service, cancellationToken);
        return new CloudConnectionStatus(true, ProviderName, accountEmail, "Google Drive is connected.");
    }

    public async Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!HasClientSecrets(out var secretsPath))
        {
            var missingStatus = new CloudConnectionStatus(
                false,
                ProviderName,
                null,
                $"Google Drive OAuth client secrets were not found at {secretsPath}.");
            return new CloudConnectionResult(false, missingStatus, missingStatus.Message);
        }

        if (!HasSharedFolder(out var sharedFolderMessage))
        {
            var missingStatus = new CloudConnectionStatus(false, ProviderName, null, sharedFolderMessage);
            return new CloudConnectionResult(false, missingStatus, sharedFolderMessage);
        }

        try
        {
            var service = await GetOrCreateServiceAsync(interactive: true, cancellationToken);
            accountEmail = await LoadAccountEmailAsync(service, cancellationToken);

            var status = new CloudConnectionStatus(true, ProviderName, accountEmail, "Google Drive is connected.");
            logger.Information(AppLogKeyword.CloudProvider, "Connected Google Drive account {AccountEmail}", accountEmail ?? "unknown");
            return new CloudConnectionResult(true, status, "Google Drive connected.");
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Google Drive connection failed");
            var status = new CloudConnectionStatus(false, ProviderName, null, "Google Drive connection failed.");
            return new CloudConnectionResult(false, status, $"Google Drive connection failed: {exception.Message}");
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        driveService?.Dispose();
        driveService = null;
        accountEmail = null;
        return Task.CompletedTask;
    }

    public async Task<CloudSetupTestResult> TestSharedFolderAsync(string sharedFolderId, CancellationToken cancellationToken = default)
    {
        if (!HasClientSecrets(out var secretsPath))
        {
            return new CloudSetupTestResult(false, $"Google Drive OAuth client secrets were not found at {secretsPath}.");
        }

        if (string.IsNullOrWhiteSpace(sharedFolderId))
        {
            return new CloudSetupTestResult(false, "Paste a Google Drive shared folder link or folder ID first.");
        }

        try
        {
            var service = await GetOrCreateServiceAsync(interactive: true, cancellationToken);
            var folder = await GetAndValidateSharedRootFolderAsync(service, sharedFolderId, cancellationToken);
            return new CloudSetupTestResult(true, $"Connected. SaveHarbor can edit shared folder '{folder.Name}'.");
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Google Drive shared folder test failed");
            return new CloudSetupTestResult(false, $"Shared folder test failed: {exception.Message}");
        }
    }

    public async Task<CloudWorldManifest?> GetWorldManifestAsync(string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var worldFolderId = await FindWorldFolderIdAsync(service, worldId, cancellationToken);
        if (worldFolderId is null)
        {
            return null;
        }

        return await DownloadJsonByNameAsync<CloudWorldManifest>(service, worldFolderId, "manifest.json", cancellationToken);
    }

    public async Task<CloudSessionLock?> GetSessionLockAsync(string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var locksFolderId = await FindFolderPathAsync(service, ["worlds", worldId, "locks"], createMissing: false, cancellationToken);
        if (locksFolderId is null)
        {
            return null;
        }

        return await DownloadJsonByNameAsync<CloudSessionLock>(service, locksFolderId, "active-session.json", cancellationToken);
    }

    public async Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (!System.IO.File.Exists(request.ArchivePath))
        {
            return new CloudUploadResult(false, null, "Upload archive was not found.");
        }

        var service = RequireConnectedService();
        var worldFolderId = await FindFolderPathAsync(service, ["worlds", request.World.WorldId], createMissing: true, cancellationToken);
        var versionsFolderId = await FindFolderPathAsync(service, ["worlds", request.World.WorldId, "versions"], createMissing: true, cancellationToken);

        if (worldFolderId is null || versionsFolderId is null)
        {
            return new CloudUploadResult(false, null, "Google Drive world folder could not be prepared.");
        }

        await UploadFileByNameAsync(service, versionsFolderId, request.VersionMetadata.ArchiveFileName, request.ArchivePath, ZipMimeType, cancellationToken);
        await UploadJsonByNameAsync(
            service,
            versionsFolderId,
            Path.ChangeExtension(request.VersionMetadata.ArchiveFileName, ".json"),
            request.VersionMetadata,
            cancellationToken);

        var manifest = request.PreviousManifest ?? new CloudWorldManifest
        {
            Provider = ProviderName,
            Game = "Windrose",
            WorldId = request.World.WorldId,
            WorldName = request.World.WorldName
        };

        manifest.Provider = ProviderName;
        manifest.WorldId = request.World.WorldId;
        manifest.WorldName = request.World.WorldName;
        manifest.LatestVersion = request.VersionMetadata;
        manifest.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await UploadJsonByNameAsync(service, worldFolderId, "manifest.json", manifest, cancellationToken);

        return new CloudUploadResult(true, manifest, $"Uploaded {request.World.WorldName} v{request.VersionMetadata.VersionNumber}.");
    }

    public async Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var versionsFolderId = await FindFolderPathAsync(service, ["worlds", request.World.WorldId, "versions"], createMissing: false, cancellationToken);
        if (versionsFolderId is null)
        {
            return new CloudDownloadResult(false, null, "Google Drive versions folder was not found.");
        }

        var archiveId = await FindFileIdByNameAsync(service, versionsFolderId, request.Version.ArchiveFileName, cancellationToken);
        if (archiveId is null)
        {
            return new CloudDownloadResult(false, null, "Cloud archive was not found.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetArchivePath)!);
        await using var stream = System.IO.File.Create(request.TargetArchivePath);
        var downloadRequest = service.Files.Get(archiveId);
        downloadRequest.SupportsAllDrives = true;
        var download = await downloadRequest.DownloadAsync(stream, cancellationToken);
        if (download.Status != DownloadStatus.Completed)
        {
            return new CloudDownloadResult(false, null, $"Google Drive download failed: {download.Exception?.Message ?? download.Status.ToString()}");
        }

        return new CloudDownloadResult(true, request.TargetArchivePath, $"Downloaded {request.World.WorldName} v{request.Version.VersionNumber}.");
    }

    public async Task WriteSessionLockAsync(CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var locksFolderId = await FindFolderPathAsync(service, ["worlds", sessionLock.WorldId, "locks"], createMissing: true, cancellationToken);
        if (locksFolderId is null)
        {
            throw new InvalidOperationException("Google Drive lock folder could not be prepared.");
        }

        await UploadJsonByNameAsync(service, locksFolderId, "active-session.json", sessionLock, cancellationToken);
    }

    public async Task ClearSessionLockAsync(string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var locksFolderId = await FindFolderPathAsync(service, ["worlds", worldId, "locks"], createMissing: false, cancellationToken);
        if (locksFolderId is null)
        {
            return;
        }

        var lockFileId = await FindFileIdByNameAsync(service, locksFolderId, "active-session.json", cancellationToken);
        if (lockFileId is not null)
        {
            var deleteRequest = service.Files.Delete(lockFileId);
            deleteRequest.SupportsAllDrives = true;
            await deleteRequest.ExecuteAsync(cancellationToken);
        }
    }

    private bool HasClientSecrets(out string secretsPath)
    {
        secretsPath = options.ResolveGoogleClientSecretsPath(pathProvider);
        return System.IO.File.Exists(secretsPath);
    }

    private bool HasSharedFolder(out string message)
    {
        if (options.HasGoogleSharedFolder)
        {
            message = "Google Drive shared folder is configured.";
            return true;
        }

        message = "Google Drive shared folder is not configured. Create one shared SaveHarbor folder, share it with your friends, then set SaveHarbor:CloudProvider:GoogleSharedFolderId.";
        return false;
    }

    private DriveService RequireConnectedService()
    {
        if (driveService is not null)
        {
            return driveService;
        }

        throw new InvalidOperationException("Google Drive is not connected. Use Connect first.");
    }

    private async Task<DriveService> GetOrCreateServiceAsync(bool interactive, CancellationToken cancellationToken)
    {
        if (driveService is not null)
        {
            return driveService;
        }

        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (driveService is not null)
            {
                return driveService;
            }

            var credential = interactive
                ? await CreateInteractiveCredentialAsync(cancellationToken)
                : await CreateSilentCredentialAsync(cancellationToken);

            driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SaveHarbor"
            });

            await EnsureSharedRootFolderAsync(driveService, cancellationToken);
            return driveService;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task<UserCredential> CreateInteractiveCredentialAsync(CancellationToken cancellationToken)
    {
        var secrets = LoadClientSecrets();
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            "SaveHarbor",
            cancellationToken,
            new FileDataStore(pathProvider.GoogleTokenStorePath, fullPath: true));
    }

    private async Task<UserCredential> CreateSilentCredentialAsync(CancellationToken cancellationToken)
    {
        var secrets = LoadClientSecrets();
        var dataStore = new FileDataStore(pathProvider.GoogleTokenStorePath, fullPath: true);
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = dataStore
        });

        var token = await flow.LoadTokenAsync("SaveHarbor", cancellationToken);
        if (token is null)
        {
            throw new InvalidOperationException("No saved Google Drive token exists.");
        }

        if (!HasRequiredScopes(token.Scope))
        {
            throw new InvalidOperationException("Saved Google Drive token uses old permissions. Use Connect again to approve shared-folder sync.");
        }

        var credential = new UserCredential(flow, "SaveHarbor", token);
        if (credential.Token.IsStale && !await credential.RefreshTokenAsync(cancellationToken))
        {
            throw new InvalidOperationException("Saved Google Drive token could not be refreshed.");
        }

        return credential;
    }

    private ClientSecrets LoadClientSecrets()
    {
        var secretsPath = options.ResolveGoogleClientSecretsPath(pathProvider);
        using var stream = System.IO.File.OpenRead(secretsPath);
        return GoogleClientSecrets.FromStream(stream).Secrets;
    }

    private static bool HasRequiredScopes(string? grantedScopes)
    {
        return !string.IsNullOrWhiteSpace(grantedScopes) &&
               grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Any(scope => string.Equals(scope, DriveService.Scope.Drive, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> LoadAccountEmailAsync(DriveService service, CancellationToken cancellationToken)
    {
        try
        {
            var request = service.About.Get();
            request.Fields = "user(emailAddress,displayName)";
            var about = await request.ExecuteAsync(cancellationToken);
            return about.User?.EmailAddress ?? about.User?.DisplayName;
        }
        catch (Exception exception)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Could not read Google Drive account info: {Message}", exception.Message);
            return null;
        }
    }

    private async Task<string> EnsureSharedRootFolderAsync(DriveService service, CancellationToken cancellationToken)
    {
        var sharedFolderId = options.ResolveGoogleSharedFolderId();
        var folder = await GetAndValidateSharedRootFolderAsync(service, sharedFolderId, cancellationToken);

        logger.Information(
            AppLogKeyword.CloudProvider,
            "Using Google Drive shared folder {FolderName} ({FolderId})",
            folder.Name,
            folder.Id);

        return folder.Id;
    }

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
        IReadOnlyList<string> relativePath,
        bool createMissing,
        CancellationToken cancellationToken)
    {
        var currentParentId = options.ResolveGoogleSharedFolderId();
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

    private async Task<string?> FindWorldFolderIdAsync(DriveService service, string worldId, CancellationToken cancellationToken)
    {
        return await FindFolderPathAsync(service, ["worlds", worldId], createMissing: false, cancellationToken);
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

    private static string EscapeQueryValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }
}
