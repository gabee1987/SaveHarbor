using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class TestPathProvider(TempDirectory root) : IAppDataPathProvider
{
    public string AppDataRoot => root.Combine("appdata");

    public string LocalLogsPath => root.Combine("appdata", "logs");

    public string LocalTestCloudRoot => root.Combine("appdata", "local-test-cloud");

    public string CloudLogsPath => root.Combine("appdata", "cloud-logs");

    public string GoogleTokenStorePath => root.Combine("appdata", "google-drive-token");

    public string GoogleClientSecretsPath => root.Combine("appdata", "google-client-secret.json");

    public string CloudProviderSettingsPath => root.Combine("appdata", "cloud-provider-settings.json");

    public string AppSettingsPath => root.Combine("appdata", "app-settings.json");
}
