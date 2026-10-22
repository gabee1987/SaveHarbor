namespace SaveHarbor.App.Domain;

public sealed record CloudUploadRequest(
    GameWorld World,
    string ArchivePath,
    CloudVersionMetadata VersionMetadata,
    CloudWorldManifest? PreviousManifest);
