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

public sealed partial class GoogleDriveCloudProvider : ICloudProvider, ISharedFolderCloudProvider
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string JsonMimeType = "application/json";
    private const string ZipMimeType = "application/zip";

    private static readonly string[] Scopes = [DriveService.Scope.Drive];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAppDataPathProvider pathProvider;
    private readonly CloudProviderOptions options;
    private readonly IPlayerIdentity playerIdentity;
    private readonly IAppLogger logger;
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    private DriveService? driveService;
    private string? accountEmail;

    public GoogleDriveCloudProvider(
        IAppDataPathProvider pathProvider,
        CloudProviderOptions options,
        IPlayerIdentity playerIdentity,
        IAppLogger logger)
    {
        this.pathProvider = pathProvider;
        this.options = options;
        this.playerIdentity = playerIdentity;
        this.logger = logger;
    }

    public string ProviderName => "Google Drive";

    public async Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        if (!HasClientSecrets(out var secretsPath))
        {
            return new CloudConnectionStatus(
                false,
                ProviderName,
                null,
                $"Google Drive is not configured. Put OAuth client secrets at {secretsPath} or set SaveHarbor:CloudProvider:GoogleClientSecretsPath.");
        }

        if (!HasSharedFolder(game, out var sharedFolderMessage))
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

        try
        {
            await EnsureGameFolderAsync(service, game, writeMarker: true, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Google Drive folder check failed for {Game}: {Message}", game, exception.Message);
            return new CloudConnectionStatus(false, ProviderName, accountEmail, exception is InvalidOperationException ? exception.Message : "Google Drive shared folder could not be checked. Use Setup to verify the folder.");
        }

        return new CloudConnectionStatus(true, ProviderName, accountEmail, "Google Drive is connected.");
    }

    public async Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
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

        if (!HasSharedFolder(game, out var sharedFolderMessage))
        {
            var missingStatus = new CloudConnectionStatus(false, ProviderName, null, sharedFolderMessage);
            return new CloudConnectionResult(false, missingStatus, sharedFolderMessage);
        }

        try
        {
            var service = await GetOrCreateServiceAsync(interactive: true, cancellationToken);
            accountEmail = await LoadAccountEmailAsync(service, cancellationToken);
            await EnsureGameFolderAsync(service, game, writeMarker: true, cancellationToken);

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
        verifiedGames.Clear();
        return Task.CompletedTask;
    }

    public async Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string sharedFolderId, CancellationToken cancellationToken = default)
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
            await EnsureGameFolderAsync(service, game, sharedFolderId, writeMarker: false, cancellationToken);
            return new CloudSetupTestResult(true, $"Connected. SaveHarbor can edit shared folder '{folder.Name}'.");
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Google Drive shared folder test failed");
            return new CloudSetupTestResult(false, $"Shared folder test failed: {exception.Message}");
        }
    }

    public async Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var worldFolderId = await FindWorldFolderIdAsync(service, game, worldId, cancellationToken);
        if (worldFolderId is null)
        {
            return null;
        }

        var manifest = await DownloadJsonByNameAsync<CloudWorldManifest>(service, worldFolderId, "manifest.json", cancellationToken);
        if (manifest is not null && !BelongsToGame(manifest, game))
        {
            throw new InvalidDataException($"The cloud manifest for this world belongs to another game, not {game}.");
        }

        return manifest;
    }

    public async Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var worldsFolderId = await FindFolderPathAsync(service, game, ["worlds"], createMissing: false, cancellationToken);
        if (worldsFolderId is null)
        {
            return [];
        }

        var worldFolders = await ListChildFoldersAsync(service, worldsFolderId, cancellationToken);
        var manifests = new List<CloudWorldManifest>();
        foreach (var worldFolder in worldFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await DownloadJsonByNameAsync<CloudWorldManifest>(service, worldFolder.Id, "manifest.json", cancellationToken);
            if (manifest?.LatestVersion is not null
                && !string.IsNullOrWhiteSpace(manifest.WorldId)
                && BelongsToGame(manifest, game))
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    public async Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var locksFolderId = await FindFolderPathAsync(service, game, ["worlds", worldId, "locks"], createMissing: false, cancellationToken);
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
        await EnsureGameFolderAsync(service, request.World.Game, writeMarker: true, cancellationToken);
        var worldFolderId = await FindFolderPathAsync(service, request.World.Game, ["worlds", request.World.WorldId], createMissing: true, cancellationToken);
        var versionsFolderId = await FindFolderPathAsync(service, request.World.Game, ["worlds", request.World.WorldId, "versions"], createMissing: true, cancellationToken);

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
            Game = request.World.Game.ToStorageKey(),
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
        var versionsFolderId = await FindFolderPathAsync(service, request.World.Game, ["worlds", request.World.WorldId, "versions"], createMissing: false, cancellationToken);
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

    public async Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        await EnsureGameFolderAsync(service, game, writeMarker: true, cancellationToken);
        var locksFolderId = await FindFolderPathAsync(service, game, ["worlds", sessionLock.WorldId, "locks"], createMissing: true, cancellationToken);
        if (locksFolderId is null)
        {
            throw new InvalidOperationException("Google Drive lock folder could not be prepared.");
        }

        await UploadJsonByNameAsync(service, locksFolderId, "active-session.json", sessionLock, cancellationToken);
    }

    public async Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        var service = RequireConnectedService();
        var locksFolderId = await FindFolderPathAsync(service, game, ["worlds", worldId, "locks"], createMissing: false, cancellationToken);
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

    private bool HasSharedFolder(GameId game, out string message)
    {
        if (options.HasSharedFolder(game))
        {
            message = "Google Drive shared folder is configured.";
            return true;
        }

        message = $"Google Drive shared folder for {game} is not configured. Use Setup to paste the folder link.";
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
}
