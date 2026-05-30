namespace SaveHarbor.App.Infrastructure;

public sealed class GameLauncherOptions
{
    public const string SectionName = "SaveHarbor:GameLauncher";

    public string LaunchUri { get; init; } = "steam://rungameid/3041230";

    public string ExecutablePath { get; init; } = string.Empty;
}
