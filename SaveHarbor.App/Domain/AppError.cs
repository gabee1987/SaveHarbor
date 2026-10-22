namespace SaveHarbor.App.Domain;

public interface IAppError
{
    string ErrorId { get; }

    AppErrorCode Code { get; }

    AppLogKeyword Keyword { get; }

    string Operation { get; }

    string UserMessage { get; }

    string TechnicalMessage { get; }

    bool IsRetryable { get; }
}

public sealed record AppError(
    string ErrorId,
    AppErrorCode Code,
    AppLogKeyword Keyword,
    string Operation,
    string UserMessage,
    string TechnicalMessage,
    bool IsRetryable) : IAppError;
