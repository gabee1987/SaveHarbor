using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IGameLauncherService
{
    Task<GameLaunchResult> LaunchAsync(CancellationToken cancellationToken = default);
}
