using System.Diagnostics;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindroseGameLauncherService(
    GameLauncherOptions options,
    IAppLogger logger) : IGameLauncherService
{
    public Task<GameLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? options.LaunchUri
            : options.ExecutablePath;

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(new GameLaunchResult(false, "Windrose launcher is not configured."));
        }

        try
        {
            logger.Information(AppLogKeyword.GameLauncher, "Launching Windrose with target {Target}", target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });

            return Task.FromResult(new GameLaunchResult(true, "Windrose launch requested."));
        }
        catch (Exception ex)
        {
            logger.Error(AppLogKeyword.GameLauncher, ex, "Windrose launch failed for target {Target}", target);
            return Task.FromResult(new GameLaunchResult(false, "Could not launch Windrose. Check that Steam is installed, or configure an executable path."));
        }
    }
}
