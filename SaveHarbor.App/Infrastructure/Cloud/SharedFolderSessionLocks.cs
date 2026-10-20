using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Cloud;

// Lock rules for providers that keep the lock as a file in a shared folder. A file write cannot be conditional, so
// acquiring writes, waits for a competing write to land, and reads back: the lock id left in the file is the winner.
internal static class SharedFolderSessionLocks
{
    public static readonly TimeSpan DefaultVerifyDelay = TimeSpan.FromSeconds(1.5);

    public static async Task<CloudLockAttempt> TryAcquireAsync(
        Func<CancellationToken, Task<CloudSessionLock?>> read,
        Func<CloudSessionLock, CancellationToken, Task> write,
        CloudSessionLock requested,
        TimeSpan verifyDelay,
        CancellationToken cancellationToken)
    {
        var current = await read(cancellationToken);
        if (current is not null && current.IsLiveAt(DateTimeOffset.UtcNow))
        {
            var ownLock = current.IsHeldBy(requested.DeviceId, requested.MachineName);
            return new CloudLockAttempt(ownLock, current);
        }

        await write(requested, cancellationToken);
        if (verifyDelay > TimeSpan.Zero)
        {
            await Task.Delay(verifyDelay, cancellationToken);
        }

        var stored = await read(cancellationToken);
        return stored is not null && string.Equals(stored.LockId, requested.LockId, StringComparison.Ordinal)
            ? new CloudLockAttempt(true, stored)
            : new CloudLockAttempt(false, stored ?? requested);
    }

    public static async Task<CloudSessionLock?> HeartbeatAsync(
        Func<CancellationToken, Task<CloudSessionLock?>> read,
        Func<CloudSessionLock, CancellationToken, Task> write,
        string lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var current = await read(cancellationToken);
        if (current is null || !string.Equals(current.LockId, lockId, StringComparison.Ordinal))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        current.LastHeartbeatAtUtc = now;
        current.ExpiresAtUtc = now + ttl;
        await write(current, cancellationToken);
        return current;
    }

    // Shared folders have no query for all locks, so each world's lock file is read; a few at a time keeps a large
    // world list from taking one round trip per world in sequence.
    public static async Task<CloudGroupSnapshot> ComposeSnapshotAsync(
        IReadOnlyList<CloudWorldManifest> manifests,
        Func<string, CancellationToken, Task<CloudSessionLock?>> readLock,
        int maxParallelReads,
        CancellationToken cancellationToken)
    {
        var locks = new Dictionary<string, CloudSessionLock>(StringComparer.OrdinalIgnoreCase);
        var gate = new Lock();
        // Manifests come from friends: an id that could not be a folder name is not read.
        var worldIds = manifests
            .Select(manifest => manifest.WorldId)
            .Where(SafePath.IsSafeSegment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await Parallel.ForEachAsync(
            worldIds,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelReads, CancellationToken = cancellationToken },
            async (worldId, token) =>
            {
                if (await readLock(worldId, token) is { } sessionLock)
                {
                    lock (gate)
                    {
                        locks[worldId] = sessionLock;
                    }
                }
            });

        return new CloudGroupSnapshot(null, manifests, locks, []);
    }
}
