namespace SaveHarbor.App.Domain;

public sealed record CloudDownloadResult(
    bool IsSuccess,
    string? ArchivePath,
    string Message);
