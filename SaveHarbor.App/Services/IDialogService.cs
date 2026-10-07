using SaveHarbor.App.Domain;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
    bool Confirm(string title, string message);
    bool ConfirmDanger(string title, string message, string actionText);
    string? SelectZipFile(string initialDirectory);
    string? SelectImportFile(string initialDirectory, string saveFileFilter);
    void ShowWorldInspector(WorldInspectorViewModel viewModel);
    string? SelectFolder(string title, string initialDirectory);
    CloudFolderChoice? ConfigureCloudFolder(
        GameId game,
        string gameDisplayName,
        string currentFolderId,
        IReadOnlyList<SavedCloudFolder> savedFolders,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync,
        Func<string, Task> forgetSavedFolderAsync);
}

// The tested folder link or ID, and the folder's name in Google Drive when the test reported it.
public sealed record CloudFolderChoice(string Input, string? FolderName);
