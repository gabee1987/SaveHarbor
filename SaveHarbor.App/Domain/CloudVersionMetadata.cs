namespace SaveHarbor.App.Domain;

public sealed class CloudVersionMetadata
{
    public int VersionNumber { get; set; }
    public string VersionId { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string UploaderMachine { get; set; } = string.Empty;
    public string ArchiveFileName { get; set; } = string.Empty;
    public string ArchiveSha256 { get; set; } = string.Empty;
    public long ArchiveSizeBytes { get; set; }
    public DateTimeOffset SourceWorldModifiedAtUtc { get; set; }
    public int BasedOnVersionNumber { get; set; }
    public string BasedOnVersionId { get; set; } = string.Empty;
}
