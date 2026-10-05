namespace SaveHarbor.App.Domain;

public sealed record GameSaveRoot(
    GameId Game,
    string RootId,
    string RootPath,
    string WorldsPath,
    string Detail,
    DateTimeOffset LastModifiedAt);
