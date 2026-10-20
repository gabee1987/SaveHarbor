namespace SaveHarbor.App.Domain;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string LastGame { get; set; } = string.Empty;

    // "GoogleDrive" or "Supabase". Empty means no cloud provider has been chosen on this PC yet.
    public string CloudProvider { get; set; } = string.Empty;

    // Identifies this installation in session locks. Generated once, never changes.
    public string DeviceId { get; set; } = string.Empty;

    // This PC's name for friends. Empty means the Windows computer name.
    public string DeviceLabel { get; set; } = string.Empty;

    // Name shown to friends in sessions and uploads. Empty means the Windows user name (the previous behaviour).
    public string PlayerName { get; set; } = string.Empty;

    // Keep only the newest N backups per game. 0 keeps every backup.
    public int BackupRetentionCount { get; set; }

    // Keep only the newest N shared versions of a world in the cloud, applied after this player's uploads.
    // 0 keeps every version (the previous behaviour).
    public int CloudVersionRetentionCount { get; set; }

    public bool AmbientEffects { get; set; } = true;

    // Per-game save folder chosen in Settings (storage key -> path). Overrides appsettings.json.
    public Dictionary<string, string> SaveRootOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
