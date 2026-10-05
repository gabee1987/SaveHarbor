using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

public sealed class CloudSyncServiceStatusTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly CloudSyncService service;

    public CloudSyncServiceStatusTests()
    {
        stateService = new LocalJsonSyncStateService(new TestPathProvider(temp));
        service = new CloudSyncService(provider, stateService, new StubBackupService(), new StubGameRegistry(), new FixedPlayerIdentity(), new NullAppLogger());
    }

    public void Dispose() => temp.Dispose();

    private static GameWorld CreateWorld(GameId game = GameId.Windrose) =>
        new(game, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);

    private static CloudWorldManifest CreateManifest(GameId game, int version) => new()
    {
        Game = game.ToStorageKey(),
        WorldId = "TEST_WORLD_ID",
        WorldName = "Test World",
        LatestVersion = new CloudVersionMetadata
        {
            VersionNumber = version,
            VersionId = $"v{version}",
            UploadedBy = "TEST_USER"
        }
    };

    private async Task SetLocalBaseAsync(GameWorld world, int? baseVersion)
    {
        var state = await stateService.LoadAsync(world, TestContext.Current.CancellationToken);
        state.LocalBaseVersionNumber = baseVersion;
        await stateService.SaveAsync(state, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefreshStatusAsync_ProviderDisconnected_ReturnsNotConnected()
    {
        provider.IsConnected = false;

        var status = await service.RefreshStatusAsync(CreateWorld(), TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.NotConnected, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_NoManifest_ReturnsNoCloudSave()
    {
        var status = await service.RefreshStatusAsync(CreateWorld(), TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.ConnectedNoCloudSave, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_ForeignActiveLock_ReturnsSomeonePlaying()
    {
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 2));
        provider.SeedLock(GameId.Windrose, new CloudSessionLock
        {
            WorldId = "TEST_WORLD_ID",
            PlayerName = "TEST_USER",
            MachineName = "TEST_OTHER_MACHINE",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var status = await service.RefreshStatusAsync(CreateWorld(), TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.SomeonePlaying, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_ExpiredForeignLock_IsIgnored()
    {
        var world = CreateWorld();
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 2));
        provider.SeedLock(GameId.Windrose, new CloudSessionLock
        {
            WorldId = "TEST_WORLD_ID",
            MachineName = "TEST_OTHER_MACHINE",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await SetLocalBaseAsync(world, 2);

        var status = await service.RefreshStatusAsync(world, TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.UpToDate, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_NoLocalBase_ReturnsCloudNewer()
    {
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 1));

        var status = await service.RefreshStatusAsync(CreateWorld(), TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.CloudNewer, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_LocalBaseOlder_ReturnsCloudNewer()
    {
        var world = CreateWorld();
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 3));
        await SetLocalBaseAsync(world, 2);

        var status = await service.RefreshStatusAsync(world, TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.CloudNewer, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_LocalBaseNewer_ReturnsConflict()
    {
        var world = CreateWorld();
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 2));
        await SetLocalBaseAsync(world, 3);

        var status = await service.RefreshStatusAsync(world, TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.Conflict, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_LocalBaseEqual_ReturnsUpToDate()
    {
        var world = CreateWorld();
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 2));
        await SetLocalBaseAsync(world, 2);

        var status = await service.RefreshStatusAsync(world, TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.UpToDate, status.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_OtherGamesManifest_IsInvisible()
    {
        provider.SeedManifest(GameId.Windrose, CreateManifest(GameId.Windrose, 2));

        var status = await service.RefreshStatusAsync(CreateWorld(GameId.Dragonwilds), TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncState.ConnectedNoCloudSave, status.State);
    }
}
