using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Serilog;

namespace SaveHarbor.App.Infrastructure;

public sealed class SerilogAppLogger : IAppLogger
{
    public void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
        LoggerFor(keyword).Information(messageTemplate, propertyValues);
    }

    public void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
        LoggerFor(keyword).Warning(messageTemplate, propertyValues);
    }

    public void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues)
    {
        LoggerFor(keyword).Error(exception, messageTemplate, propertyValues);
    }

    private static ILogger LoggerFor(AppLogKeyword keyword)
    {
        return Log.ForContext("Keyword", keyword.ToString());
    }
}
