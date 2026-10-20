namespace SaveHarbor.App.Domain;

// Everything the world list needs for one game in one read. ServerTimeUtc is null when the provider has no clock of its
// own (shared folders); lock expiry is then judged by this PC's clock.
public sealed record CloudGroupSnapshot(
    DateTimeOffset? ServerTimeUtc,
    IReadOnlyList<CloudWorldManifest> Manifests,
    IReadOnlyDictionary<string, CloudSessionLock> LocksByWorldId,
    IReadOnlyList<CloudMember> Members);

public sealed record CloudMember(string Id, string DisplayName, string Role);
