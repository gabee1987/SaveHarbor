using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.Tests.Support;

// The cloud parts the Sharing page reads: storage use and unused files. The rest of syncing is not its business.
public sealed class StubCloudSyncService : ICloudSyncService
{
    public CloudStorageUsage? Usage { get; set; }

    public CloudUnusedFiles Unused { get; set; } = new([], 0);

    public List<string> Calls { get; } = [];

    public Task<CloudStorageUsage?> GetStorageUsageAsync(GameId game, CancellationToken cancellationToken = default) => Task.FromResult(Usage);

    public Task<CloudUnusedFiles> FindUnusedFilesAsync(GameId game, CancellationToken cancellationToken = default) => Task.FromResult(Unused);

    public Task<CloudSyncResult> RemoveUnusedFilesAsync(GameId game, CloudUnusedFiles files, CancellationToken cancellationToken = default)
    {
        Calls.Add("remove-unused");
        return Task.FromResult(new CloudSyncResult(true, CloudSyncState.UpToDate, $"Deleted {files.Count} file(s)."));
    }

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncStatus> RefreshStatusAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> DownloadLatestAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> DownloadLatestAvailableAsync(GameSaveRoot profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> DownloadCloudWorldAsync(GameSaveRoot profile, string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> RemoveCloudWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudWorldOverview> GetOverviewAsync(GameId game, IReadOnlyList<GameWorld> localWorlds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudRefresh> RefreshAllAsync(GameId game, IReadOnlyList<GameWorld> localWorlds, GameWorld? selectedWorld, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> UploadCurrentAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> StartSessionAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> HeartbeatSessionAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<CloudSyncResult> EndSessionAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<DateTimeOffset?> GetPendingUploadAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ClearPendingUploadAsync(GameWorld world, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// Google Drive folders for the Sharing page's Drive screen.
public sealed class StubCloudSetupService : ICloudSetupService
{
    public string FolderId { get; set; } = string.Empty;

    public List<SavedCloudFolder> Saved { get; } = [];

    public string GetCurrentSharedFolderId(GameId game) => FolderId;

    public bool HasSharedFolderConfigured(GameId game) => FolderId.Length > 0;

    public IReadOnlyList<SavedCloudFolder> GetSavedFolders(GameId game) => Saved;

    public Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveSharedFolderAsync(GameId game, string input, string? folderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ForgetSavedFolderAsync(GameId game, string folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// Answers every confirmation with Answer and remembers what was asked.
public sealed class RecordingDialogService : IDialogService
{
    public bool Answer { get; set; } = true;

    public List<string> Asked { get; } = [];

    public List<string> Errors { get; } = [];

    public void ShowInfo(string title, string message) => Asked.Add(title);

    public void ShowError(string title, string message) => Errors.Add(message);

    public bool Confirm(string title, string message)
    {
        Asked.Add(title);
        return Answer;
    }

    public bool ConfirmDanger(string title, string message, string actionText) => Confirm(title, message);

    public string? SelectZipFile(string initialDirectory) => null;

    public string? SelectImportFile(string initialDirectory, string saveFileFilter) => null;

    public void ShowWorldInspector(WorldInspectorViewModel viewModel, Uri skinDictionary)
    {
    }

    public void ShowPatchNotes(PatchNotesViewModel viewModel, Uri skinDictionary)
    {
    }

    public string? SelectFolder(string title, string initialDirectory) => null;

    public CloudFolderChoice? ConfigureCloudFolder(
        Uri skinDictionary,
        string gameDisplayName,
        string currentFolderId,
        IReadOnlyList<SavedCloudFolder> savedFolders,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync,
        Func<string, Task> forgetSavedFolderAsync) => null;

    public bool ShowReleaseNotes(string title, string markdown, bool canSkip) => false;
}

public sealed class RecordingToastService : IToastService
{
    public List<(ToastKind Kind, string Title)> Shown { get; } = [];

    public event EventHandler<ToastNotification>? ToastRequested
    {
        add { }
        remove { }
    }

    public void Show(ToastKind kind, string title, string message) => Shown.Add((kind, title));

    public void Info(string title, string message) => Show(ToastKind.Info, title, message);

    public void Success(string title, string message) => Show(ToastKind.Success, title, message);

    public void Warning(string title, string message) => Show(ToastKind.Warning, title, message);

    public void Error(string title, string message) => Show(ToastKind.Error, title, message);
}
