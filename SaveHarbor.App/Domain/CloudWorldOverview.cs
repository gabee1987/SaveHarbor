using System.Text.Json.Serialization;

namespace SaveHarbor.App.Domain;

// Sync state of one local world, as shown in the world list. Session locks are not read for the list; the selected
// world's full status includes them.
public sealed record WorldSyncSummary(
    string WorldId,
    CloudSyncState State,
    bool HasLocalChanges,
    int? LocalBaseVersion,
    CloudVersionMetadata? LatestVersion);

// Every world of a game: the local ones with their sync state, and the shared ones that are not on this PC. Locks are
// the session locks of the snapshot by world id, so the window can tell when a friend starts or finishes playing; the
// cached overview leaves them out.
public sealed record CloudWorldOverview(
    bool IsConnected,
    IReadOnlyList<WorldSyncSummary> LocalWorlds,
    IReadOnlyList<CloudWorldManifest> CloudOnlyWorlds,
    IReadOnlyDictionary<string, CloudSessionLock>? Locks = null)
{
    public static CloudWorldOverview NotConnected { get; } = new(false, [], []);

    [JsonIgnore]
    public IReadOnlyDictionary<string, CloudSessionLock> LocksByWorldId => Locks ?? EmptyLocks;

    private static readonly IReadOnlyDictionary<string, CloudSessionLock> EmptyLocks = new Dictionary<string, CloudSessionLock>();
}

// What one cloud refresh read: the provider's connection, every world of the game, and the selected world's state.
public sealed record CloudRefresh(
    CloudConnectionStatus Connection,
    CloudWorldOverview Overview,
    CloudSyncStatus? SelectedStatus);
