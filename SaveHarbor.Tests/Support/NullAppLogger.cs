using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class NullAppLogger : IAppLogger
{
    public void Debug(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues)
    {
    }

    public void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues)
    {
    }
}
