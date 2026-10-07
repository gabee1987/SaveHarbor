using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudSetupService
{
    string GetCurrentSharedFolderId(GameId game);

    bool HasSharedFolderConfigured(GameId game);

    // Folders used before for this game, the current one first.
    IReadOnlyList<SavedCloudFolder> GetSavedFolders(GameId game);

    Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default);

    Task SaveSharedFolderAsync(GameId game, string input, string? folderName, CancellationToken cancellationToken = default);

    Task ForgetSavedFolderAsync(GameId game, string folderId, CancellationToken cancellationToken = default);
}

public sealed record CloudSetupTestResult(bool IsSuccess, string Message, string? FolderName = null);
