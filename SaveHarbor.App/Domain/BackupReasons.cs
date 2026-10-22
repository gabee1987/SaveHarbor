namespace SaveHarbor.App.Domain;

// The reason part of a backup file name (see Utilities.BackupFileName).
public static class BackupReasons
{
    public const string Manual = "manual";
    public const string PreRestore = "pre-restore";
    public const string PreImport = "pre-import";
    public const string Imported = "imported";
    public const string CloudUpload = "cloud-upload";

    // Made before a world is removed from this PC, and the last cloud version of a world removed from the cloud.
    public const string PreRemove = "pre-remove";
    public const string CloudRemoved = "cloud-removed";
}
