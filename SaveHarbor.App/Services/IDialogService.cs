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
    void ShowWorldInspector(WorldInspectorViewModel viewModel, Uri skinDictionary);
    string? SelectFolder(string title, string initialDirectory);
    CloudFolderChoice? ConfigureCloudFolder(
        Uri skinDictionary,
        string gameDisplayName,
        string currentFolderId,
        IReadOnlyList<SavedCloudFolder> savedFolders,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync,
        Func<string, Task> forgetSavedFolderAsync);

    // The provider kind chosen, or null for "Decide later".
    string? ChooseCloudProvider(Uri skinDictionary, IReadOnlyList<CloudProviderChoice> choices);

    // The group chosen, or null when the dialog was closed before that (the player may still have signed in).
    CloudGroupSetupResult? ConfigureCloudGroup(Uri skinDictionary, string gameDisplayName, CloudGroupSetupViewModel viewModel);

    // True when the player chose "Skip this version"; offered only when canSkip.
    bool ShowReleaseNotes(string title, string markdown, bool canSkip);
}

// One card of the provider choice; its texts are the CloudProviderChoice.<Kind>* keys.
public sealed record CloudProviderChoice(string Kind, bool IsRecommended);

// The tested folder link or ID, and the folder's name in Google Drive when the test reported it.
public sealed record CloudFolderChoice(string Input, string? FolderName);
