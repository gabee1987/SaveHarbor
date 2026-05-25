namespace SaveHarbor.App.Domain;

public sealed record WindroseProfile(
    string ProfileId,
    string ProfilePath,
    string RocksDbVersion,
    string WorldsPath,
    DateTimeOffset LastModifiedAt);
