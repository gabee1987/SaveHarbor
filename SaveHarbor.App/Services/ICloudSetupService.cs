namespace SaveHarbor.App.Services;

public interface ICloudSetupService
{
    string CurrentSharedFolderId { get; }

    bool HasSharedFolderConfigured { get; }

    string NormalizeSharedFolderInput(string input);

    Task<CloudSetupTestResult> TestSharedFolderAsync(string input, CancellationToken cancellationToken = default);

    Task SaveSharedFolderAsync(string input, CancellationToken cancellationToken = default);
}

public sealed record CloudSetupTestResult(bool IsSuccess, string Message);
