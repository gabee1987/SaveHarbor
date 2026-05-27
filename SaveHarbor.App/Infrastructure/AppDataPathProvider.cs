using System.IO;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveHarbor");

    public string LocalLogsPath => Path.Combine(AppDataRoot, "logs");

    public string LocalTestCloudRoot => Path.Combine(AppDataRoot, "cloud-test");

    public string CloudLogsPath => Path.Combine(LocalTestCloudRoot, "logs");

    public string GoogleTokenStorePath => Path.Combine(AppDataRoot, "google-drive-token");

    public string GoogleClientSecretsPath => Path.Combine(AppDataRoot, "google-client-secret.json");

    public string CloudProviderSettingsPath => Path.Combine(AppDataRoot, "cloud-provider-settings.json");
}
