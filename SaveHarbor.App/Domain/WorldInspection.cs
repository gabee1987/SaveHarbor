namespace SaveHarbor.App.Domain;

public sealed record InspectionItem(string Label, string Value);

public sealed record InspectionSection(string Title, IReadOnlyList<InspectionItem> Items);

// A world save picked from outside the save folder (a friend's file, a game-made backup copy).
// World.WorldId is the id it would be imported as; World.SavePath is the picked source.
// Fingerprint identifies the world itself (not the file name), so two copies of one world can be recognised.
public sealed record ImportCandidate(
    GameWorld World,
    string? Fingerprint,
    long? Revision,
    DateTimeOffset? SavedAtUtc,
    IReadOnlyList<InspectionItem> Summary);
