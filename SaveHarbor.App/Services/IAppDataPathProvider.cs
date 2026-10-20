using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppDataPathProvider
{
    string AppDataRoot { get; }

    string LocalLogsPath { get; }

    string GetBackupRoot(GameId game);

    // State shared by every cloud provider (for example the Windrose world ledger).
    string GetSyncStateRoot(GameId game);

    // Per-world sync state of one provider; version numbers differ between providers. providerKey comes from
    // CloudProviderKind.ToStorageKey.
    string GetSyncStateRoot(GameId game, string providerKey);

    string GetLocalTestCloudRoot(GameId game);

    string LegacyBackupRoot { get; }

    string LegacySyncStateRoot { get; }

    string LegacyLocalTestCloudRoot { get; }

    string CloudLogsPath { get; }

    string GoogleTokenStorePath { get; }

    string GoogleClientSecretsPath { get; }

    string CloudProviderSettingsPath { get; }

    string AppSettingsPath { get; }
}
