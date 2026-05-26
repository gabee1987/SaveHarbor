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
    Error
}
