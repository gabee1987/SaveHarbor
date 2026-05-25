namespace SaveHarbor.App.Domain;

public sealed record BackupInfo(
    string FilePath,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes);
