namespace SaveHarbor.App.Domain;

public sealed record CloudSyncResult(
    bool IsSuccess,
    CloudSyncState State,
    string Message);
