namespace SaveHarbor.App.Domain;

public sealed class LocalSyncState
{
    public int SchemaVersion { get; set; } = 1;
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string LocalWorldPath { get; set; } = string.Empty;
    public int? LastKnownCloudVersionNumber { get; set; }
    public string LastKnownCloudVersionId { get; set; } = string.Empty;
    public int? LocalBaseVersionNumber { get; set; }
    public string LocalBaseVersionId { get; set; } = string.Empty;
    public DateTimeOffset? LastDownloadedAtUtc { get; set; }
    public DateTimeOffset? LastUploadedAtUtc { get; set; }
    public string LastLocalBackupPath { get; set; } = string.Empty;
    public DateTimeOffset? LastCloudCheckAtUtc { get; set; }

    public static LocalSyncState CreateNew(WindroseWorld world)
    {
        return new LocalSyncState
        {
            WorldId = world.WorldId,
            WorldName = world.WorldName,
            LocalWorldPath = world.SavePath
        };
    }
}
