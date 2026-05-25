namespace SaveHarbor.App.Domain;

public sealed record WindroseWorld(
    string WorldId,
    string WorldName,
    string WorldPresetType,
    string SavePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    long SizeBytes,
    int FileCount);
