using System.Diagnostics;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class SteamGameLauncherService(IAppLogger logger) : IGameLauncherService
{
    public Task<GameLaunchResult> LaunchAsync(IGameDefinition game, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = string.IsNullOrWhiteSpace(game.ExecutablePath)
            ? game.LaunchUri
            : game.ExecutablePath;

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(new GameLaunchResult(false, $"{game.DisplayName} launcher is not configured."));
        }

        try
        {
            logger.Information(AppLogKeyword.GameLauncher, "Launching {Game} with target {Target}", game.DisplayName, target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });

            return Task.FromResult(new GameLaunchResult(true, $"{game.DisplayName} launch requested."));
        }
        catch (Exception ex)
        {
            logger.Error(AppLogKeyword.GameLauncher, ex, "{Game} launch failed for target {Target}", game.DisplayName, target);
            return Task.FromResult(new GameLaunchResult(false, $"Could not launch {game.DisplayName}. Check that Steam is installed, or configure an executable path."));
        }
    }
}
