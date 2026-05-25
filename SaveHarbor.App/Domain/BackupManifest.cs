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
}
