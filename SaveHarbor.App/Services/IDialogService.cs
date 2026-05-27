namespace SaveHarbor.App.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
    bool Confirm(string title, string message);
    string? SelectZipFile(string initialDirectory);
    string? ConfigureCloudFolder(
        string currentFolderId,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync);
}
