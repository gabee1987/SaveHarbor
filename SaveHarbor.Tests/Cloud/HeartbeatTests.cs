using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// A running session renews its lock once per heartbeat interval; losing the lock is reported as a conflict.
public sealed class HeartbeatTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly LocalJsonSyncStateService stateService;
    private readonly CloudSyncService service;

    public HeartbeatTests()
    {
        var paths = new TestPathProvider(temp);
        stateService = new LocalJsonSyncStateService(paths, new FixedProviderSelector());
        service = new CloudSyncService(
            provider,
            stateService,
            new StubBackupService(),
            new StubGameRegistry(),
            new FixedPlayerIdentity(),
            new AppSettingsStore(paths),
            new NullAppLogger(),
            new FixedDeviceIdentity(),
            new CloudProviderOptions(),
            clock);
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GameWorld World { get; } =
        new(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);

    private int HeartbeatCalls => provider.Calls.Count(call => call.StartsWith("HeartbeatSessionLock", StringComparison.Ordinal));

    private async Task StartSessionAsync()
    {
        provider.SeedManifest(GameId.Windrose, new CloudWorldManifest
        {
            Game = "windrose",
            WorldId = World.WorldId,
            LatestVersion = new CloudVersionMetadata { VersionNumber = 1, VersionId = "v1", UploadedBy = "TEST_USER" }
        });
        var state = await stateService.LoadAsync(World, Token);
        state.LocalBaseVersionNumber = 1;
        await stateService.SaveAsync(state, Token);
        Assert.True((await service.StartSessionAsync(World, Token)).IsSuccess);
    }

    [Fact]
    public async Task Heartbeat_WithinInterval_DoesNotReachTheCloud()
    {
        await StartSessionAsync();
        clock.Advance(TimeSpan.FromMinutes(4));

        var result = await service.HeartbeatSessionAsync(World, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, HeartbeatCalls);
    }

    [Fact]
    public async Task Heartbeat_AfterInterval_RenewsWithLockTtlOnce()
    {
        await StartSessionAsync();
        clock.Advance(TimeSpan.FromMinutes(5));

        var first = await service.HeartbeatSessionAsync(World, Token);
        var second = await service.HeartbeatSessionAsync(World, Token);

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(1, HeartbeatCalls);
        Assert.Equal(TimeSpan.FromSeconds(1200), provider.LastHeartbeatTtl);
    }

    [Fact]
    public async Task Heartbeat_AfterAppRestart_RenewsImmediately()
    {
        provider.SeedLock(GameId.Windrose, new CloudSessionLock
        {
            LockId = "TEST_LOCK_ID",
            WorldId = World.WorldId,
            DeviceId = FixedDeviceIdentity.TestDeviceId,
            ExpiresAtUtc = clock.Now.AddMinutes(10)
        });

        var result = await service.HeartbeatSessionAsync(World, Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, HeartbeatCalls);
    }

    [Fact]
    public async Task Heartbeat_LockTakenOver_ReturnsConflict()
    {
        await StartSessionAsync();
        provider.SeedLock(GameId.Windrose, new CloudSessionLock
        {
            LockId = "TEST_OTHER_LOCK_ID",
            WorldId = World.WorldId,
            PlayerName = "TEST_FRIEND",
            DeviceId = "00000000-0000-4000-8000-000000067890",
            ExpiresAtUtc = clock.Now.AddMinutes(20)
        });
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = await service.HeartbeatSessionAsync(World, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudSyncState.Conflict, result.State);
        Assert.Contains("another player took over", result.Message);
        Assert.Equal(0, HeartbeatCalls);
    }

    [Fact]
    public async Task Heartbeat_LockRemoved_ReturnsConflict()
    {
        await StartSessionAsync();
        await provider.ClearSessionLockAsync(GameId.Windrose, World.WorldId, string.Empty, Token);
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = await service.HeartbeatSessionAsync(World, Token);

        Assert.Equal(CloudSyncState.Conflict, result.State);
    }
}
