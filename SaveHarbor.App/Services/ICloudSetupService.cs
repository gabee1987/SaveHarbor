using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface ICloudSetupService
{
    string GetCurrentSharedFolderId(GameId game);

    bool HasSharedFolderConfigured(GameId game);

    Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default);

    Task SaveSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default);
}

public sealed record CloudSetupTestResult(bool IsSuccess, string Message);
