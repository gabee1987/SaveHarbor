namespace SaveHarbor.App.Domain;

public sealed record WorldBackupItem(string FilePath, DateTimeOffset CreatedAt, string Reason, long SizeBytes);
