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
}
