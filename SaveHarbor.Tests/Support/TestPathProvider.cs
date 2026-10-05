using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class TestPathProvider(TempDirectory root) : IAppDataPathProvider
{
    public string AppDataRoot => root.Combine("appdata");

    public string LocalLogsPath => root.Combine("appdata", "logs");

    public string LegacyBackupRoot => root.Combine("appdata", "backups");

    public string LegacySyncStateRoot => root.Combine("appdata", "sync-state");

    public string LegacyLocalTestCloudRoot => root.Combine("appdata", "cloud-test");

    public string GetBackupRoot(GameId game) => Path.Combine(LegacyBackupRoot, game.ToStorageKey());

    public string GetSyncStateRoot(GameId game) => Path.Combine(LegacySyncStateRoot, game.ToStorageKey());

    public string GetLocalTestCloudRoot(GameId game) => Path.Combine(LegacyLocalTestCloudRoot, game.ToStorageKey());

    public string CloudLogsPath => root.Combine("appdata", "cloud-logs");

    public string GoogleTokenStorePath => root.Combine("appdata", "google-drive-token");

    public string GoogleClientSecretsPath => root.Combine("appdata", "google-client-secret.json");

    public string CloudProviderSettingsPath => root.Combine("appdata", "cloud-provider-settings.json");

    public string AppSettingsPath => root.Combine("appdata", "app-settings.json");
}
