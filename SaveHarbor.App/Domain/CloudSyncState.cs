namespace SaveHarbor.App.Domain;

public enum CloudSyncState
{
    NotConnected,
    ConnectedNoCloudSave,
    UpToDate,
    CloudNewer,
    LocalNewerUploadSafe,
    SomeonePlaying,
    Conflict,
    AuthError,
    Error,

    // The cloud or this world's shared files need a newer SaveHarbor; nothing is uploaded or downloaded.
    UpdateRequired
}
