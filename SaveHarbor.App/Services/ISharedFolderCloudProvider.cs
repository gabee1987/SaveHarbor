namespace SaveHarbor.App.Services;

public interface ISharedFolderCloudProvider
{
    Task<CloudSetupTestResult> TestSharedFolderAsync(string sharedFolderId, CancellationToken cancellationToken = default);
}
