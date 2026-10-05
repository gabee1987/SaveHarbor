namespace SaveHarbor.App.Domain;

public sealed record GameWorld(
    GameId Game,
    string WorldId,
    string WorldName,
    string Subtitle,
    string SavePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    long SizeBytes,
    int FileCount);
