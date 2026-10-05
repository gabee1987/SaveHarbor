namespace SaveHarbor.App.Domain;

public sealed class CloudWorldManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Provider { get; set; } = string.Empty;
    // Legacy manifests written before multi-game support have no Game and are Windrose.
    public string Game { get; set; } = "Windrose";
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public CloudVersionMetadata? LatestVersion { get; set; }
    public CloudRetentionSettings Retention { get; set; } = new();
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CloudRetentionSettings
{
    public int KeepLatestVersions { get; set; } = 20;
}
