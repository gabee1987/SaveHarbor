using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class FakeCloudProvider : ICloudProvider
{
    private readonly Dictionary<(GameId Game, string WorldId), CloudWorldManifest> manifests = [];
    private readonly Dictionary<(GameId Game, string WorldId), CloudSessionLock> locks = [];

    public string ProviderName => "Fake cloud";

    public CloudProviderCapabilities Capabilities { get; set; } = new(true, false, false, true, false, false, long.MaxValue, 0);

    // Clock the snapshot reports; null like a shared folder.
    public DateTimeOffset? ServerTimeUtc { get; set; }

    public bool IsConnected { get; set; } = true;

    public List<string> Calls { get; } = [];

    // Version archive names in the fake cloud, oldest first. Uploads add to it.
    public List<string> StoredVersions { get; } = [];

    // Decides lock acquisition instead of the default rule (a live lock of another device wins).
    public Func<CloudSessionLock, CloudLockAttempt>? AcquireOutcome { get; set; }

    public TimeSpan? LastHeartbeatTtl { get; private set; }

    // The next N uploads fail.
    public int FailUploadTimes { get; set; }

    // When set, downloads copy this archive to the requested path; otherwise they fail.
    public string? ArchiveToServe { get; set; }

    public void SeedManifest(GameId game, CloudWorldManifest manifest)
    {
        manifests[(game, manifest.WorldId)] = manifest;
    }

    public void SeedLock(GameId game, CloudSessionLock sessionLock)
    {
        locks[(game, sessionLock.WorldId)] = sessionLock;
    }

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetConnectionStatus:{game}");
        return Task.FromResult(IsConnected
            ? new CloudConnectionStatus(true, ProviderName, "TEST_USER", "Connected.")
            : CloudConnectionStatus.NotConnected(ProviderName));
    }

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Connect:{game}");
        var status = new CloudConnectionStatus(IsConnected, ProviderName, "TEST_USER", "Connected.");
        return Task.FromResult(new CloudConnectionResult(IsConnected, status, status.Message));
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("Disconnect");
        return Task.CompletedTask;
    }

    public Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetWorldManifest:{game}:{worldId}");
        return Task.FromResult(manifests.GetValueOrDefault((game, worldId)));
    }

    public Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListWorldManifests:{game}");
        IReadOnlyList<CloudWorldManifest> result = manifests
            .Where(pair => pair.Key.Game == game)
            .Select(pair => pair.Value)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetSessionLock:{game}:{worldId}");
        return Task.FromResult(locks.GetValueOrDefault((game, worldId)));
    }

    public Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetSnapshot:{game}");
        IReadOnlyList<CloudWorldManifest> gameManifests = manifests
            .Where(pair => pair.Key.Game == game)
            .Select(pair => pair.Value)
            .ToArray();
        var gameLocks = locks
            .Where(pair => pair.Key.Game == game)
            .ToDictionary(pair => pair.Key.WorldId, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(new CloudGroupSnapshot(ServerTimeUtc, gameManifests, gameLocks, []));
    }

    public Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Upload:{request.World.Game}:{request.World.WorldId}");
        if (FailUploadTimes > 0)
        {
            FailUploadTimes--;
            return Task.FromResult(new CloudUploadResult(false, null, "Simulated upload failure."));
        }

        var manifest = request.PreviousManifest ?? new CloudWorldManifest
        {
            Provider = ProviderName,
            Game = request.World.Game.ToStorageKey(),
            WorldId = request.World.WorldId,
            WorldName = request.World.WorldName
        };
        manifest.LatestVersion = request.VersionMetadata;
        manifests[(request.World.Game, request.World.WorldId)] = manifest;
        StoredVersions.Add(request.VersionMetadata.ArchiveFileName);
        return Task.FromResult(new CloudUploadResult(true, manifest, "Uploaded."));
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Download:{request.World.Game}:{request.World.WorldId}");
        if (ArchiveToServe is null)
        {
            return Task.FromResult(new CloudDownloadResult(false, null, "Not supported by the fake provider."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetArchivePath)!);
        File.Copy(ArchiveToServe, request.TargetArchivePath, overwrite: true);
        return Task.FromResult(new CloudDownloadResult(true, request.TargetArchivePath, "Downloaded."));
    }

    public Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CloudStoredVersion> result = StoredVersions.Select(name => new CloudStoredVersion(name)).ToArray();
        return Task.FromResult(result);
    }

    public Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"RemoveVersion:{archiveFileName}");
        StoredVersions.Remove(archiveFileName);
        return Task.CompletedTask;
    }

    public Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"RemoveWorld:{game}:{worldId}");
        manifests.Remove((game, worldId));
        locks.Remove((game, worldId));
        StoredVersions.Clear();
        return Task.FromResult(new CloudRemovalResult(true, "Removed."));
    }

    public Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TryAcquireSessionLock:{game}:{requested.WorldId}");
        var key = (game, requested.WorldId);
        var attempt = AcquireOutcome?.Invoke(requested)
            ?? (locks.TryGetValue(key, out var current)
                && current.IsLiveAt(requested.StartedAtUtc)
                && !current.IsHeldBy(requested.DeviceId, requested.MachineName)
                    ? new CloudLockAttempt(false, current)
                    : new CloudLockAttempt(true, requested));
        if (attempt.Acquired)
        {
            locks[key] = attempt.Current;
        }

        return Task.FromResult(attempt);
    }

    public Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        Calls.Add($"HeartbeatSessionLock:{game}:{worldId}");
        LastHeartbeatTtl = ttl;
        if (!locks.TryGetValue((game, worldId), out var current) || current.LockId != lockId)
        {
            return Task.FromResult<CloudSessionLock?>(null);
        }

        current.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
        current.ExpiresAtUtc = current.LastHeartbeatAtUtc + ttl;
        return Task.FromResult<CloudSessionLock?>(current);
    }

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ClearSessionLock:{game}:{worldId}");
        locks.Remove((game, worldId));
        return Task.CompletedTask;
    }
}
