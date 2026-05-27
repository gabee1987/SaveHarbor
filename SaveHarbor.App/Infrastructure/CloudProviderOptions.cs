using System.IO;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class CloudProviderOptions
{
    public const string SectionName = "SaveHarbor:CloudProvider";

    public string Provider { get; set; } = CloudProviderKind.GoogleDrive;

    public string GoogleAppFolderName { get; set; } = "SaveHarbor";

    public string GoogleClientSecretsPath { get; set; } = string.Empty;

    public string GoogleSharedFolderId { get; set; } = string.Empty;

    public bool HasGoogleSharedFolder => !string.IsNullOrWhiteSpace(GoogleSharedFolderId);

    public string ResolveGoogleSharedFolderId()
    {
        var value = GoogleSharedFolderId.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var foldersMarkerIndex = value.IndexOf("/folders/", StringComparison.OrdinalIgnoreCase);
        if (foldersMarkerIndex >= 0)
        {
            value = value[(foldersMarkerIndex + "/folders/".Length)..];
        }

        var queryIndex = value.IndexOfAny(['?', '&']);
        if (queryIndex >= 0)
        {
            value = value[..queryIndex];
        }

        return value.Trim().Trim('/');
    }

    public string ResolveGoogleClientSecretsPath(IAppDataPathProvider pathProvider)
    {
        if (!string.IsNullOrWhiteSpace(GoogleClientSecretsPath))
        {
            var configuredPath = Environment.ExpandEnvironmentVariables(GoogleClientSecretsPath);
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);
        }

        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Configuration", "google-client-secret.json");
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        return pathProvider.GoogleClientSecretsPath;
    }
}

public static class CloudProviderKind
{
    public const string GoogleDrive = "GoogleDrive";
    public const string LocalTest = "LocalTest";
}
