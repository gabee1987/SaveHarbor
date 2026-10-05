using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IGameLauncherService
{
    Task<GameLaunchResult> LaunchAsync(IGameDefinition game, CancellationToken cancellationToken = default);
}
