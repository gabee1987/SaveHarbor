using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Serilog;
using Serilog.Events;

namespace SaveHarbor.App.Infrastructure;

public sealed class SerilogAppLogger : IAppLogger
{
    private readonly AppLoggingOptions options;
    private readonly IActiveGameContext activeGame;

    public SerilogAppLogger(AppLoggingOptions options, IActiveGameContext activeGame)
    {
        this.options = options;
        this.activeGame = activeGame;
    }

    public void Debug(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
        if (options.IsEnabled(keyword, LogEventLevel.Debug))
        {
            LoggerFor(keyword).Debug(messageTemplate, propertyValues);
        }
    }

    public void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
        if (options.IsEnabled(keyword, LogEventLevel.Information))
        {
            LoggerFor(keyword).Information(messageTemplate, propertyValues);
        }
    }

    public void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
        if (options.IsEnabled(keyword, LogEventLevel.Warning))
        {
            LoggerFor(keyword).Warning(messageTemplate, propertyValues);
        }
    }

    public void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues)
    {
        if (options.IsEnabled(keyword, LogEventLevel.Error))
        {
            LoggerFor(keyword).Error(exception, messageTemplate, propertyValues);
        }
    }

    private ILogger LoggerFor(AppLogKeyword keyword)
    {
        return Log.ForContext("Game", activeGame.Current.StorageKey).ForContext("Keyword", keyword.ToString());
    }
}
