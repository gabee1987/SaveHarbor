namespace SaveHarbor.App.Services;

public interface IAppDataPathProvider
{
    string AppDataRoot { get; }

    string LocalLogsPath { get; }

    string LocalTestCloudRoot { get; }

    string CloudLogsPath { get; }

    string GoogleTokenStorePath { get; }

    string GoogleClientSecretsPath { get; }

    string CloudProviderSettingsPath { get; }
}
