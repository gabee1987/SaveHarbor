namespace SaveHarbor.App.Domain;

public sealed record CloudUploadResult(
    bool IsSuccess,
    CloudWorldManifest? Manifest,
    string Message);
