namespace SaveHarbor.App.Services;

public interface IAppDataPathProvider
{
    string AppDataRoot { get; }

    string LocalLogsPath { get; }

    string LocalTestCloudRoot { get; }

    string CloudLogsPath { get; }
}
