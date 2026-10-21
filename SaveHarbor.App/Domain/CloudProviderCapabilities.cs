namespace SaveHarbor.App.Domain;

// What the active cloud provider can do, so callers stop assuming Google Drive.
public sealed record CloudProviderCapabilities(
    bool HasSharedFolders,
    bool HasGroups,
    bool HasAtomicLocks,
    bool SupportsHeartbeat,
    bool SupportsRealtime,
    bool HasRecycleBin,
    long MaxArchiveBytes,
    // Shared versions kept per world when "Keep shared versions" is 0. 0 keeps every version.
    int DefaultRetentionCount)
{
    public static CloudProviderCapabilities None { get; } = new(false, false, false, false, false, false, long.MaxValue, 0);
}
