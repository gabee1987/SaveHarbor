using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// Starting a session goes through the provider's acquire, and a lock belongs to this PC by device id, or by computer
// name for locks written before device ids existed.
public sealed class CloudSyncServiceLockTests : IDisposable
{
    private const string OtherDeviceId = "00000000-0000-4000-8000-000000067890";

    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly LocalJsonSyncStateService stateService;
    private readonly AppSettingsStore settings;
    private readonly CloudSyncService service;

    public CloudSyncServiceLockTests()
    {
        var paths = new TestPathProvider(temp);
        stateService = new LocalJsonSyncStateService(paths, new FixedProviderSelector());
        settings = new AppSettingsStore(paths);
        service = new CloudSyncService(
            provider,
            stateService,
            new StubBackupService(),
            new StubGameRegistry(),
            new FixedPlayerIdentity(),
            settings,
            new NullAppLogger(),
            new FixedDeviceIdentity(),
            new CloudProviderOptions(),
            clock);
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GameWorld World { get; } =
        new(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);

    private async Task SeedSharedWorldOnLatestAsync()
    {
        provider.SeedManifest(GameId.Windrose, new CloudWorldManifest
        {
            Game = "windrose",
            WorldId = World.WorldId,
            WorldName = World.WorldName,
            LatestVersion = new CloudVersionMetadata { VersionNumber = 3, VersionId = "v3", UploadedBy = "TEST_USER" }
        });
        var state = await stateService.LoadAsync(World, Token);
        state.LocalBaseVersionNumber = 3;
        await stateService.SaveAsync(state, Token);
    }

    private CloudSessionLock Lock(string deviceId, string machineName, TimeSpan expiresIn) => new()
    {
        LockId = Guid.NewGuid().ToString("D"),
        WorldId = World.WorldId,
        PlayerName = "TEST_FRIEND",
        MachineName = machineName,
        DeviceId = deviceId,
        BasedOnVersionNumber = 3,
        StartedAtUtc = clock.Now,
        ExpiresAtUtc = clock.Now + expiresIn
    };

    [Fact]
    public async Task Start_Acquires_LockWithDeviceIdAndTwentyMinuteTtl()
    {
        await SeedSharedWorldOnLatestAsync();

        var result = await service.StartSessionAsync(World, Token);

        Assert.True(result.IsSuccess, result.Message);
        var stored = await provider.GetSessionLockAsync(GameId.Windrose, World.WorldId, Token);
        Assert.NotNull(stored);
        Assert.Equal(FixedDeviceIdentity.TestDeviceId, stored.DeviceId);
        Assert.Equal(2, stored.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(1200), stored.ExpiresAtUtc - stored.StartedAtUtc);
    }

    [Fact]
    public async Task Start_AcquireLostToAnotherPlayer_ReturnsSomeonePlayingWithTheirName()
    {
        await SeedSharedWorldOnLatestAsync();
        var winner = Lock(OtherDeviceId, "TEST_OTHER_PC", TimeSpan.FromMinutes(20));
        provider.AcquireOutcome = _ => new CloudLockAttempt(false, winner);

        var result = await service.StartSessionAsync(World, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudSyncState.SomeonePlaying, result.State);
        Assert.Contains("TEST_FRIEND", result.Message);
    }

    [Fact]
    public async Task Start_LiveForeignLock_IsBlockedWithoutTryingToAcquire()
    {
        await SeedSharedWorldOnLatestAsync();
        provider.SeedLock(GameId.Windrose, Lock(OtherDeviceId, "TEST_OTHER_PC", TimeSpan.FromMinutes(5)));

        var result = await service.StartSessionAsync(World, Token);

        Assert.Equal(CloudSyncState.SomeonePlaying, result.State);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("TryAcquireSessionLock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_ExpiredForeignLock_IsTakenOver()
    {
        await SeedSharedWorldOnLatestAsync();
        provider.SeedLock(GameId.Windrose, Lock(OtherDeviceId, "TEST_OTHER_PC", TimeSpan.FromMinutes(-1)));

        var result = await service.StartSessionAsync(World, Token);

        Assert.True(result.IsSuccess, result.Message);
        var stored = await provider.GetSessionLockAsync(GameId.Windrose, World.WorldId, Token);
        Assert.Equal(FixedDeviceIdentity.TestDeviceId, stored!.DeviceId);
    }

    [Fact]
    public async Task Start_UnconfirmedOwnWrite_AsksToRetry()
    {
        await SeedSharedWorldOnLatestAsync();
        provider.AcquireOutcome = requested => new CloudLockAttempt(false, requested);

        var result = await service.StartSessionAsync(World, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudSyncState.Error, result.State);
    }

    [Theory]
    [InlineData(FixedDeviceIdentity.TestDeviceId, "TEST_OTHER_PC", true)]   // same device, renamed PC
    [InlineData("", "*", true)]                                            // legacy lock from this computer
    [InlineData(OtherDeviceId, "*", false)]                                // another Windows user on this computer
    [InlineData("", "TEST_OTHER_PC", false)]                               // legacy lock from another computer
    public async Task Status_RecognisesOwnLock(string deviceId, string machineName, bool expectedOwn)
    {
        await SeedSharedWorldOnLatestAsync();
        provider.SeedLock(GameId.Windrose, Lock(deviceId, machineName == "*" ? Environment.MachineName : machineName, TimeSpan.FromMinutes(10)));

        var status = await service.RefreshStatusAsync(World, Token);

        Assert.Equal(expectedOwn, status.IsOwnSession);
        Assert.Equal(expectedOwn ? CloudSyncState.UpToDate : CloudSyncState.SomeonePlaying, status.State);
    }

    [Fact]
    public async Task Overview_UsesProviderClockForLockExpiry()
    {
        await SeedSharedWorldOnLatestAsync();
        provider.SeedLock(GameId.Windrose, Lock(OtherDeviceId, "TEST_OTHER_PC", TimeSpan.FromMinutes(10)));

        var byLocalClock = await service.GetOverviewAsync(GameId.Windrose, [World], Token);
        provider.ServerTimeUtc = clock.Now + TimeSpan.FromHours(1);
        var byServerClock = await service.GetOverviewAsync(GameId.Windrose, [World], Token);

        Assert.Equal(CloudSyncState.SomeonePlaying, Assert.Single(byLocalClock.LocalWorlds).State);
        Assert.Equal(CloudSyncState.UpToDate, Assert.Single(byServerClock.LocalWorlds).State);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("ListWorldManifests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_RetentionZero_UsesProviderDefault()
    {
        provider.Capabilities = provider.Capabilities with { DefaultRetentionCount = 1 };
        provider.StoredVersions.AddRange(["20260101_000000_TEST_USER_v1.zip", "20260101_000000_TEST_USER_v2.zip"]);
        var harness = new BackupHarness();
        try
        {
            var stateForUpload = new LocalJsonSyncStateService(harness.Paths, new FixedProviderSelector());
            var uploader = new CloudSyncService(
                provider,
                stateForUpload,
                harness.Backups,
                new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, harness.DragonwildsAdapter)),
                new FixedPlayerIdentity(),
                harness.Settings,
                new NullAppLogger(),
                new FixedDeviceIdentity(),
                new CloudProviderOptions());
            provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
            {
                Game = "dragonwilds",
                WorldId = "TEST_WORLD",
                WorldName = "TEST_WORLD",
                LatestVersion = new CloudVersionMetadata { VersionNumber = 2, VersionId = "v2", UploadedBy = "TEST_USER" }
            });
            var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
            var state = await stateForUpload.LoadAsync(world, Token);
            state.LocalBaseVersionNumber = 2;
            await stateForUpload.SaveAsync(state, Token);

            var result = await uploader.UploadCurrentAsync(world, Token);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(3, new CloudStoredVersion(Assert.Single(provider.StoredVersions)).VersionNumber);
        }
        finally
        {
            harness.Dispose();
        }
    }
}
