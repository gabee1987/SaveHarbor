namespace SaveHarbor.App.Domain;

public sealed class BackupManifest
{
    public int SchemaVersion { get; set; }
    public string Game { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;

    // Schema version 2. Version 1 manifests carry neither field and describe a directory payload.
    public string PayloadKind { get; set; } = nameof(WorldPayloadKind.Directory);
    public List<BackupFileEntry> Files { get; set; } = [];
}

public sealed class BackupFileEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
