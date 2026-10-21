namespace SaveHarbor.App.Infrastructure.Updates;

public sealed class UpdateOptions
{
    public const string SectionName = "SaveHarbor:Updates";

    // The public repository whose GitHub releases carry the installer packages.
    public string GitHubRepository { get; init; } = "https://github.com/gabee1987/SaveHarbor";

    public int CheckIntervalHours { get; init; } = 6;

    public bool AutoDownload { get; init; } = true;

    // A folder of packed releases to update from instead of GitHub (--update-source); for testing an update locally.
    public string LocalSourceFolder { get; init; } = string.Empty;
}
