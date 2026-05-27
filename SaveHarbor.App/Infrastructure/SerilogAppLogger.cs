using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Serilog;
using Serilog.Events;

namespace SaveHarbor.App.Infrastructure;

public sealed class SerilogAppLogger : IAppLogger
{
    private readonly AppLoggingOptions options;

    public SerilogAppLogger(AppLoggingOptions options)
    {
        this.options = options;
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

    private static ILogger LoggerFor(AppLogKeyword keyword)
    {
        return Log.ForContext("Keyword", keyword.ToString());
    }
}
