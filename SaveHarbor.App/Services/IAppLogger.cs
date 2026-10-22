using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppLogger
{
    void Debug(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues);

    void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues);

    void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues);

    void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues);
}
