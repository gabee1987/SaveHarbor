namespace SaveHarbor.App.Domain;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string LastGame { get; set; } = string.Empty;

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
