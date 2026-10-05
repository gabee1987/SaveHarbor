using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
    bool Confirm(string title, string message);
    string? SelectZipFile(string initialDirectory);
    string? SelectImportFile(string initialDirectory, string saveFileFilter);
    void ShowWorldInspector(WorldInspectorViewModel viewModel);
    string? SelectFolder(string title, string initialDirectory);
    string? ConfigureCloudFolder(
        string gameDisplayName,
        string currentFolderId,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync);
}
