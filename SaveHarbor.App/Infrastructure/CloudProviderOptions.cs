using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class CloudProviderOptions
{
    public const string SectionName = "SaveHarbor:CloudProvider";

    // "LocalTest" forces the local folder provider for development; empty lets the user choose. Replaces the "Provider"
    // key, which is still honoured for one release when it says "LocalTest".
    public string DeveloperProviderOverride { get; set; } = string.Empty;

    public bool ForcesLocalTest =>
        string.Equals(DeveloperProviderOverride, CloudProviderKind.LocalTest, StringComparison.OrdinalIgnoreCase);

    // How long a session lock lives without a heartbeat, and how often a running session renews it.
    public int LockTtlSeconds { get; set; } = 1200;

    public int HeartbeatIntervalSeconds { get; set; } = 300;

    public SupabaseOptions Supabase { get; set; } = new();

    public string GoogleAppFolderName { get; set; } = "SaveHarbor";

    public string GoogleClientSecretsPath { get; set; } = string.Empty;

    public Dictionary<GameId, string> SharedFolderIds { get; } = new();

    public string GetSharedFolderId(GameId game) =>
        NormalizeSharedFolderInput(SharedFolderIds.GetValueOrDefault(game, string.Empty));

    public bool HasSharedFolder(GameId game) => !string.IsNullOrWhiteSpace(GetSharedFolderId(game));

    public void SetSharedFolderId(GameId game, string input) =>
        SharedFolderIds[game] = NormalizeSharedFolderInput(input);

    public static string NormalizeSharedFolderInput(string input)
    {
        var value = input.Trim();
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

        var bundledEncryptedPath = Path.Combine(AppContext.BaseDirectory, "Configuration", GoogleClientSecretsProtector.EncryptedFileName);
        if (File.Exists(bundledEncryptedPath))
        {
            return bundledEncryptedPath;
        }

        var bundledPlainPath = Path.Combine(AppContext.BaseDirectory, "Configuration", GoogleClientSecretsProtector.PlainFileName);
        if (File.Exists(bundledPlainPath))
        {
            return bundledPlainPath;
        }

        return pathProvider.GoogleClientSecretsPath;
    }
}

public static class CloudProviderKind
{
    public const string GoogleDrive = "GoogleDrive";
    public const string Supabase = "Supabase";
    public const string LocalTest = "LocalTest";

    // Folder name for state kept per provider. Nothing chosen yet counts as Google Drive, the provider that folder setup
    // leads to and that every install used before providers could be chosen.
    public static string ToStorageKey(string? kind) =>
        string.Equals(kind, Supabase, StringComparison.OrdinalIgnoreCase) ? "supabase"
        : string.Equals(kind, LocalTest, StringComparison.OrdinalIgnoreCase) ? "localtest"
        : "googledrive";
}
