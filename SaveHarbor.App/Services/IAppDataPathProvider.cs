using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppDataPathProvider
{
    string AppDataRoot { get; }

    string LocalLogsPath { get; }

    string GetBackupRoot(GameId game);

    string GetSyncStateRoot(GameId game);

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
