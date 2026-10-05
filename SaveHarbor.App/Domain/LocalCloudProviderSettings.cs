namespace SaveHarbor.App.Domain;

public sealed class LocalCloudProviderSettings
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, string> SharedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GoogleSharedFolderId { get; set; } = string.Empty; // v1 legacy: read, treated as Windrose, never written
}
