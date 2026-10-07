namespace SaveHarbor.App.Domain;

// Sync state of one local world, as shown in the world list. Session locks are not read for the list; the selected
// world's full status includes them.
public sealed record WorldSyncSummary(
    string WorldId,
    CloudSyncState State,
    bool HasLocalChanges,
    int? LocalBaseVersion,
    CloudVersionMetadata? LatestVersion);

// Every world of a game: the local ones with their sync state, and the shared ones that are not on this PC.
public sealed record CloudWorldOverview(
    bool IsConnected,
    IReadOnlyList<WorldSyncSummary> LocalWorlds,
    IReadOnlyList<CloudWorldManifest> CloudOnlyWorlds)
{
    public static CloudWorldOverview NotConnected { get; } = new(false, [], []);
}
