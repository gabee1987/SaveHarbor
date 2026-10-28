namespace SaveHarbor.App.Domain;

// IsReadOnly marks a world in a save root SaveHarbor never writes to (see GameSaveRoot.CanReceiveWorlds): it can be
// backed up and shared, but never restored over or removed.
public sealed record GameWorld(
    GameId Game,
    string WorldId,
    string WorldName,
    string Subtitle,
    string SavePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    long SizeBytes,
    int FileCount,
    bool IsReadOnly = false);
