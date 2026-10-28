namespace SaveHarbor.App.Domain;

// CanReceiveWorlds is false for a folder SaveHarbor only reads (Enshrouded's Steam Cloud folder): worlds there can be
// shared, but downloads and imports go to a root that can receive them.
public sealed record GameSaveRoot(
    GameId Game,
    string RootId,
    string RootPath,
    string WorldsPath,
    string Detail,
    DateTimeOffset LastModifiedAt,
    bool CanReceiveWorlds = true);
