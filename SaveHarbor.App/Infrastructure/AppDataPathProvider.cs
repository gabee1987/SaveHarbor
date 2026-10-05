using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveHarbor");

    public string LocalLogsPath => Path.Combine(AppDataRoot, "logs");

    public string LegacyBackupRoot => Path.Combine(AppDataRoot, "backups");

    public string LegacySyncStateRoot => Path.Combine(AppDataRoot, "sync-state");

    public string LegacyLocalTestCloudRoot => Path.Combine(AppDataRoot, "cloud-test");

    public string GetBackupRoot(GameId game) => Path.Combine(LegacyBackupRoot, game.ToStorageKey());

    public string GetSyncStateRoot(GameId game) => Path.Combine(LegacySyncStateRoot, game.ToStorageKey());

    public string GetLocalTestCloudRoot(GameId game) => Path.Combine(LegacyLocalTestCloudRoot, game.ToStorageKey());

    public string CloudLogsPath => Path.Combine(LegacyLocalTestCloudRoot, "logs");

    public string GoogleTokenStorePath => Path.Combine(AppDataRoot, "google-drive-token");

    public string GoogleClientSecretsPath => Path.Combine(AppDataRoot, "google-client-secret.json");

    public string CloudProviderSettingsPath => Path.Combine(AppDataRoot, "cloud-provider-settings.json");

    public string AppSettingsPath => Path.Combine(AppDataRoot, "app-settings.json");
}
