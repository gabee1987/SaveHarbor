using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class FakeCloudProvider : ICloudProvider
{
    private readonly Dictionary<(GameId Game, string WorldId), CloudWorldManifest> manifests = [];
    private readonly Dictionary<(GameId Game, string WorldId), CloudSessionLock> locks = [];

    public string ProviderName => "Fake cloud";

    public bool IsConnected { get; set; } = true;

    public List<string> Calls { get; } = [];

    // Runs after a lock is stored and before the write returns; simulates another client writing before our verify-read.
    public Func<Task>? AfterLockWrite { get; set; }

    // The next N uploads fail.
    public int FailUploadTimes { get; set; }

    public TimeSpan LockVerifyDelay => TimeSpan.Zero;

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
        return Task.FromResult(new CloudUploadResult(true, manifest, "Uploaded."));
    }

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Download:{request.World.Game}:{request.World.WorldId}");
        return Task.FromResult(new CloudDownloadResult(false, null, "Not supported by the fake provider."));
    }

    public async Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken cancellationToken = default)
    {
        Calls.Add($"WriteSessionLock:{game}:{sessionLock.WorldId}");
        locks[(game, sessionLock.WorldId)] = sessionLock;
        if (AfterLockWrite is not null)
        {
            await AfterLockWrite();
        }
    }

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ClearSessionLock:{game}:{worldId}");
        locks.Remove((game, worldId));
        return Task.CompletedTask;
    }
}
