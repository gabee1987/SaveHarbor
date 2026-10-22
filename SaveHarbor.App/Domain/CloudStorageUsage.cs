namespace SaveHarbor.App.Domain;

// Space the group's shared versions take. QuotaBytes is null when the provider has no fixed limit worth showing.
public sealed record CloudStorageUsage(long UsedBytes, long? QuotaBytes);

// Archives in the cloud that no shared version points to, for example after an upload that was interrupted.
public sealed record CloudUnusedFiles(IReadOnlyList<string> Paths, long TotalBytes)
{
    public static CloudUnusedFiles None { get; } = new([], 0);

    public int Count => Paths.Count;
}
