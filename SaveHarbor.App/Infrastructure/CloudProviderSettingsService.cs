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

    public string CurrentSharedFolderId => options.ResolveGoogleSharedFolderId();

    public bool HasSharedFolderConfigured => options.HasGoogleSharedFolder;

    public string NormalizeSharedFolderInput(string input)
    {
        var previous = options.GoogleSharedFolderId;
        try
        {
            options.GoogleSharedFolderId = input;
            return options.ResolveGoogleSharedFolderId();
        }
        finally
        {
            options.GoogleSharedFolderId = previous;
        }
    }

    public async Task<CloudSetupTestResult> TestSharedFolderAsync(string input, CancellationToken cancellationToken = default)
    {
        var folderId = NormalizeSharedFolderInput(input);
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
            return await sharedFolderProvider.TestSharedFolderAsync(folderId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Shared folder setup test failed");
            return new CloudSetupTestResult(false, $"Shared folder test failed: {exception.Message}");
        }
    }

    public async Task SaveSharedFolderAsync(string input, CancellationToken cancellationToken = default)
    {
        var folderId = NormalizeSharedFolderInput(input);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new InvalidOperationException("Shared folder ID cannot be empty.");
        }

        options.GoogleSharedFolderId = folderId;
        Directory.CreateDirectory(pathProvider.AppDataRoot);

        var settings = new LocalCloudProviderSettings
        {
            GoogleSharedFolderId = folderId
        };

        await File.WriteAllTextAsync(
            pathProvider.CloudProviderSettingsPath,
            JsonSerializer.Serialize(settings, JsonOptions),
            cancellationToken);

        logger.Information(AppLogKeyword.CloudProvider, "Saved Google Drive shared folder setup");
    }
}

public sealed class LocalCloudProviderSettings
{
    public string GoogleSharedFolderId { get; set; } = string.Empty;
}
