namespace SaveHarbor.App.Domain;

public sealed record ActivityLogItem(DateTimeOffset Timestamp, string Level, string Message);
