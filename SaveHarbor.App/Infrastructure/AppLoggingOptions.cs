using SaveHarbor.App.Domain;
using Serilog.Events;

namespace SaveHarbor.App.Infrastructure;

public sealed class AppLoggingOptions
{
    public const string SectionName = "SaveHarbor:Logging";

    public bool Enabled { get; init; } = true;

    public LogEventLevel DefaultMinimumLevel { get; init; } = LogEventLevel.Information;

    public int RetainedFileCountLimit { get; init; } = 14;

    public Dictionary<AppLogKeyword, LogEventLevel> KeywordMinimumLevels { get; init; } = new()
    {
        [AppLogKeyword.App] = LogEventLevel.Information,
        [AppLogKeyword.Backup] = LogEventLevel.Information,
        [AppLogKeyword.Restore] = LogEventLevel.Information,
        [AppLogKeyword.Import] = LogEventLevel.Information,
        [AppLogKeyword.CloudUpload] = LogEventLevel.Information,
        [AppLogKeyword.CloudDownload] = LogEventLevel.Information,
        [AppLogKeyword.CloudSession] = LogEventLevel.Information,
        [AppLogKeyword.GameLauncher] = LogEventLevel.Information,
        [AppLogKeyword.CloudSync] = LogEventLevel.Warning,
        [AppLogKeyword.CloudProvider] = LogEventLevel.Warning,
        [AppLogKeyword.Discovery] = LogEventLevel.Warning,
        [AppLogKeyword.Ui] = LogEventLevel.Warning
    };

    public bool IsEnabled(AppLogKeyword keyword, LogEventLevel level)
    {
        if (!Enabled)
        {
            return false;
        }

        var minimumLevel = KeywordMinimumLevels.GetValueOrDefault(keyword, DefaultMinimumLevel);
        return level >= minimumLevel;
    }
}
