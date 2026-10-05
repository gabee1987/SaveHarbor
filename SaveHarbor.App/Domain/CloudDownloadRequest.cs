namespace SaveHarbor.App.Domain;

public sealed record CloudDownloadRequest(
    GameWorld World,
    CloudVersionMetadata Version,
    string TargetArchivePath);
