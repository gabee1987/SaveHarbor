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
}
