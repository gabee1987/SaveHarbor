using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppErrorHandler
{
    AppError Handle(Exception exception, string operation, AppLogKeyword keyword);
}
