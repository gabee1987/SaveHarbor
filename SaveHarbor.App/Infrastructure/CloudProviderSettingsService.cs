using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class CloudProviderSettingsService : ICloudSetupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly CloudProviderOptions options;
    private readonly ICloudProvider cloudProvider;
    private readonly IAppDataPathProvider pathProvider;
    private readonly IAppLogger logger;

    public CloudProviderSettingsService(
        CloudProviderOptions options,
        ICloudProvider cloudProvider,
        IAppDataPathProvider pathProvider,
        IAppLogger logger)
    {
        this.options = options;
        this.cloudProvider = cloudProvider;
        this.pathProvider = pathProvider;
        this.logger = logger;
    }

    public string GetCurrentSharedFolderId(GameId game) => options.GetSharedFolderId(game);

    public bool HasSharedFolderConfigured(GameId game) => options.HasSharedFolder(game);

    public async Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default)
    {
        var folderId = CloudProviderOptions.NormalizeSharedFolderInput(input);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return new CloudSetupTestResult(false, "Paste a Google Drive shared folder link or folder ID first.");
        }

        if (cloudProvider is not ISharedFolderCloudProvider sharedFolderProvider)
        {
            return new CloudSetupTestResult(false, "The active cloud provider does not support shared folder setup.");
        }

        try
        {
            return await sharedFolderProvider.TestSharedFolderAsync(game, folderId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Shared folder setup test failed");
            return new CloudSetupTestResult(false, $"Shared folder test failed: {exception.Message}");
        }
    }

    public async Task SaveSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default)
    {
        var folderId = CloudProviderOptions.NormalizeSharedFolderInput(input);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new InvalidOperationException("Shared folder ID cannot be empty.");
        }

        Directory.CreateDirectory(pathProvider.AppDataRoot);

        var settings = ReadExistingSettings();
        settings.SchemaVersion = 2;
        settings.SharedFolders[game.ToStorageKey()] = folderId;
        if (game == GameId.Windrose)
        {
            settings.GoogleSharedFolderId = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(settings.GoogleSharedFolderId))
        {
            settings.SharedFolders.TryAdd(GameId.Windrose.ToStorageKey(), settings.GoogleSharedFolderId);
            settings.GoogleSharedFolderId = string.Empty;
        }

        var tempPath = pathProvider.CloudProviderSettingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
        File.Move(tempPath, pathProvider.CloudProviderSettingsPath, overwrite: true);

        options.SetSharedFolderId(game, folderId);
        logger.Information(AppLogKeyword.CloudProvider, "Saved Google Drive shared folder setup for {Game}", game);
    }

    private LocalCloudProviderSettings ReadExistingSettings()
    {
        try
        {
            if (File.Exists(pathProvider.CloudProviderSettingsPath))
            {
                var existing = JsonSerializer.Deserialize<LocalCloudProviderSettings>(File.ReadAllText(pathProvider.CloudProviderSettingsPath));
                if (existing is not null)
                {
                    existing.SharedFolders = new Dictionary<string, string>(existing.SharedFolders, StringComparer.OrdinalIgnoreCase);
                    return existing;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Existing cloud settings could not be read and will be replaced: {Message}", exception.Message);
        }

        return new LocalCloudProviderSettings();
    }
}
