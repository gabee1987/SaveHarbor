namespace SaveHarbor.App.Domain;

public sealed class LocalCloudProviderSettings
{
    public int SchemaVersion { get; set; } = 3;
    public Dictionary<string, string> SharedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GoogleSharedFolderId { get; set; } = string.Empty; // v1 legacy: read, treated as Windrose, never written
    public List<SavedCloudFolder> SavedFolders { get; set; } = [];
    // v3: SaveHarbor Cloud group per game (storage key -> group id).
    public Dictionary<string, string> ActiveGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
