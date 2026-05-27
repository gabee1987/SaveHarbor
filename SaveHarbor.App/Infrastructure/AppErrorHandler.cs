using System.IO;
using System.Net.Http;
using System.Text.Json;
using Google;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class AppErrorHandler : IAppErrorHandler
{
    private readonly IAppLogger logger;

    public AppErrorHandler(IAppLogger logger)
    {
        this.logger = logger;
    }

    public AppError Handle(Exception exception, string operation, AppLogKeyword keyword)
    {
        var error = CreateError(exception, operation, keyword);
        logger.Error(
            keyword,
            exception,
            "Operation failed. ErrorId={ErrorId} Code={ErrorCode} Operation={Operation}",
            error.ErrorId,
            error.Code,
            operation);

        return error;
    }

    private static AppError CreateError(Exception exception, string operation, AppLogKeyword keyword)
    {
        var code = MapCode(exception);
        var errorId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return new AppError(
            errorId,
            code,
            keyword,
            operation,
            BuildUserMessage(code),
            exception.Message,
            IsRetryable(code));
    }

    private static AppErrorCode MapCode(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => AppErrorCode.AccessDenied,
            FileNotFoundException or DirectoryNotFoundException => AppErrorCode.FileNotFound,
            JsonException => AppErrorCode.InvalidData,
            GoogleApiException or HttpRequestException or TaskCanceledException => AppErrorCode.CloudProviderFailure,
            IOException => AppErrorCode.FileSystem,
            InvalidOperationException => AppErrorCode.InvalidOperation,
            _ => AppErrorCode.Unexpected
        };
    }

    private static string BuildUserMessage(AppErrorCode code)
    {
        return code switch
        {
            AppErrorCode.AccessDenied => "SaveHarbor does not have permission to access one of the required files or folders.",
            AppErrorCode.FileNotFound => "A required file or folder could not be found.",
            AppErrorCode.FileSystem => "SaveHarbor could not complete a file operation. Check that the files are not locked and try again.",
            AppErrorCode.InvalidData => "SaveHarbor found invalid or corrupted data while reading a file.",
            AppErrorCode.InvalidOperation => "The operation cannot continue in the current state.",
            AppErrorCode.CloudProviderFailure => "SaveHarbor could not reach or update the cloud provider. Check the connection and try again.",
            _ => "SaveHarbor hit an unexpected error. Check the log for details."
        };
    }

    private static bool IsRetryable(AppErrorCode code)
    {
        return code is AppErrorCode.FileSystem or AppErrorCode.CloudProviderFailure;
    }
}
