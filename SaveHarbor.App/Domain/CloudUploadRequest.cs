namespace SaveHarbor.App.Domain;

public sealed record CloudUploadRequest(
    WindroseWorld World,
    string ArchivePath,
    CloudVersionMetadata VersionMetadata,
    CloudWorldManifest? PreviousManifest);
