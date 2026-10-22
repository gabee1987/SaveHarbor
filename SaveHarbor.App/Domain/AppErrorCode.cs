namespace SaveHarbor.App.Domain;

public enum AppErrorCode
{
    Unexpected,
    AccessDenied,
    FileNotFound,
    FileSystem,
    InvalidData,
    InvalidOperation,
    CloudNotConnected,
    CloudAuth,
    CloudConflict,
    CloudArchiveMissing,
    CloudHashMismatch,
    CloudProviderFailure
}
