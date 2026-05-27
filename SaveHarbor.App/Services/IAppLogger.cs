using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppLogger
{
    void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues);

    void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues);

    void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues);
}
