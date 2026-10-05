namespace SaveHarbor.App.Domain;

public sealed record CloudSyncStatus(
    CloudSyncState State,
    CloudConnectionStatus Connection,
    CloudWorldManifest? Manifest,
    CloudVersionMetadata? LatestVersion,
    CloudSessionLock? SessionLock,
    LocalSyncState LocalState,
    string Title,
    string Detail)
{
    public bool IsConnected => Connection.IsConnected;

    // The local world changed since it last matched its base cloud version (played without uploading).
    public bool HasLocalChanges { get; init; }
}
