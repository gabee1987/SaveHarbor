using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ISharedFolderCloudProvider
{
    Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string sharedFolderId, CancellationToken cancellationToken = default);
}
