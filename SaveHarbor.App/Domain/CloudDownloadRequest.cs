namespace SaveHarbor.App.Domain;

public sealed record CloudDownloadRequest(
    WindroseWorld World,
    CloudVersionMetadata Version,
    string TargetArchivePath);
