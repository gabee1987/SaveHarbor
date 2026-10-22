using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// The lock file rules shared by Google Drive and the local test cloud, exercised through the local test cloud.
public sealed class SharedFolderLockTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FolderCloudProvider provider;

    public SharedFolderLockTests()
    {
        provider = new FolderCloudProvider(new TestPathProvider(temp), new FixedPlayerIdentity(), TimeSpan.Zero);
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CloudSessionLock Lock(string deviceId, TimeSpan expiresIn) => new()
    {
        LockId = Guid.NewGuid().ToString("D"),
        WorldId = "TEST_WORLD_ID",
        PlayerName = "TEST_USER",
        MachineName = "TEST_PC",
        DeviceId = deviceId,
        StartedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow + expiresIn
    };

    [Fact]
    public async Task TryAcquire_FreeWorld_WritesTheLockFile()
    {
        var requested = Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(20));

        var attempt = await provider.TryAcquireSessionLockAsync(GameId.Windrose, requested, Token);

        Assert.True(attempt.Acquired);
        var stored = await provider.GetSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", Token);
        Assert.Equal(requested.LockId, stored!.LockId);
        Assert.Equal("TEST_DEVICE_A", stored.DeviceId);
    }

    [Fact]
    public async Task TryAcquire_LiveLockOfAnotherDevice_ReturnsThatLockAndLeavesIt()
    {
        var first = Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(20));
        await provider.TryAcquireSessionLockAsync(GameId.Windrose, first, Token);

        var attempt = await provider.TryAcquireSessionLockAsync(GameId.Windrose, Lock("TEST_DEVICE_B", TimeSpan.FromMinutes(20)), Token);

        Assert.False(attempt.Acquired);
        Assert.Equal(first.LockId, attempt.Current.LockId);
        Assert.Equal(first.LockId, (await provider.GetSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", Token))!.LockId);
    }

    [Fact]
    public async Task TryAcquire_ExpiredLockOfAnotherDevice_IsReplaced()
    {
        await provider.TryAcquireSessionLockAsync(GameId.Windrose, Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(-1)), Token);
        var requested = Lock("TEST_DEVICE_B", TimeSpan.FromMinutes(20));

        var attempt = await provider.TryAcquireSessionLockAsync(GameId.Windrose, requested, Token);

        Assert.True(attempt.Acquired);
        Assert.Equal(requested.LockId, attempt.Current.LockId);
    }

    [Fact]
    public async Task Heartbeat_OwnLock_ExtendsExpiryInTheFile()
    {
        var requested = Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(1));
        await provider.TryAcquireSessionLockAsync(GameId.Windrose, requested, Token);

        var renewed = await provider.HeartbeatSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", requested.LockId, TimeSpan.FromMinutes(20), Token);

        Assert.NotNull(renewed);
        var stored = await provider.GetSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", Token);
        Assert.True(stored!.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(19));
        Assert.Equal(stored.ExpiresAtUtc - TimeSpan.FromMinutes(20), stored.LastHeartbeatAtUtc);
    }

    [Fact]
    public async Task Heartbeat_ForeignOrMissingLock_ReturnsNull()
    {
        Assert.Null(await provider.HeartbeatSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", "TEST_LOCK_ID", TimeSpan.FromMinutes(20), Token));

        await provider.TryAcquireSessionLockAsync(GameId.Windrose, Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(20)), Token);

        Assert.Null(await provider.HeartbeatSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", "TEST_LOCK_ID", TimeSpan.FromMinutes(20), Token));
    }

    [Fact]
    public async Task Snapshot_ContainsManifestsAndTheirLocks()
    {
        await provider.ConnectAsync(GameId.Windrose, Token);
        var archive = temp.Combine("TEST_ARCHIVE.zip");
        await File.WriteAllTextAsync(archive, "TEST", Token);
        var world = new GameWorld(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);
        await provider.UploadVersionAsync(new CloudUploadRequest(world, archive, new CloudVersionMetadata { VersionNumber = 1, ArchiveFileName = "TEST_ARCHIVE_v1.zip" }, null), Token);
        await provider.TryAcquireSessionLockAsync(GameId.Windrose, Lock("TEST_DEVICE_A", TimeSpan.FromMinutes(20)), Token);

        var snapshot = await provider.GetSnapshotAsync(GameId.Windrose, Token);

        Assert.Null(snapshot.ServerTimeUtc);
        Assert.Equal("TEST_WORLD_ID", Assert.Single(snapshot.Manifests).WorldId);
        Assert.Equal("TEST_DEVICE_A", snapshot.LocksByWorldId["test_world_id"].DeviceId);
        Assert.Empty(snapshot.Members);
    }
}
