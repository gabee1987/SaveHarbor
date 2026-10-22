using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// One refresh reads the connection once and the group's snapshot once, and serves the selected world from it.
public sealed class CloudRefreshTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly DragonwildsSaveAdapter adapter;
    private readonly CloudSyncService service;
    private readonly string saveRoot;

    public CloudRefreshTests()
    {
        saveRoot = temp.Combine("saves");
        Directory.CreateDirectory(saveRoot);
        adapter = new DragonwildsSaveAdapter(new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Dragonwilds] = new() { SaveRoot = saveRoot }
        }));
        stateService = new LocalJsonSyncStateService(new TestPathProvider(temp), new FixedProviderSelector());
        service = new CloudSyncService(
            provider,
            stateService,
            new StubBackupService(),
            new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, adapter)),
            new FixedPlayerIdentity(),
            new AppSettingsStore(new TestPathProvider(temp)),
            new NullAppLogger(),
            new FixedDeviceIdentity(),
            new CloudProviderOptions());
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void SeedCloud(string worldId, int version) => provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
    {
        Game = "dragonwilds",
        WorldId = worldId,
        WorldName = worldId,
        LatestVersion = new CloudVersionMetadata { VersionNumber = version, VersionId = $"v{version}", UploadedBy = "TEST_USER" }
    });

    private async Task<GameWorld> LocalWorldAsync(string worldId, int baseVersion)
    {
        var path = Path.Combine(saveRoot, $"{worldId}.sav");
        File.WriteAllBytes(path, [1, 2, 3]);
        var world = (await adapter.ReadWorldAsync(path, Token))!;
        var state = await stateService.LoadAsync(world, Token);
        state.LocalBaseVersionNumber = baseVersion;
        state.LastDownloadedAtUtc = DateTimeOffset.UtcNow;
        await LocalChangeDetector.CaptureAsync(state, world, adapter, Token);
        await stateService.SaveAsync(state, Token);
        return world;
    }

    [Fact]
    public async Task RefreshAll_ReadsOneSnapshotForTheListAndTheSelectedWorld()
    {
        SeedCloud("TEST_WORLD_A", 2);
        SeedCloud("TEST_WORLD_B", 5);
        var selected = await LocalWorldAsync("TEST_WORLD_A", 2);
        var other = await LocalWorldAsync("TEST_WORLD_B", 4);
        provider.SeedLock(GameId.Dragonwilds, new CloudSessionLock
        {
            LockId = "TEST_LOCK",
            WorldId = "TEST_WORLD_A",
            PlayerName = "TEST_USER_2",
            DeviceId = "00000000-0000-4000-8000-000000067890",
            StartedAtUtc = DateTimeOffset.UtcNow,
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            BasedOnVersionNumber = 2
        });

        var refresh = await service.RefreshAllAsync(GameId.Dragonwilds, [selected, other], selected, Token);

        Assert.True(refresh.Connection.IsConnected);
        Assert.Equal(CloudSyncState.SomeonePlaying, refresh.SelectedStatus!.State);
        Assert.Equal("TEST_USER_2", refresh.SelectedStatus.SessionLock!.PlayerName);
        Assert.Equal(CloudSyncState.CloudNewer, refresh.Overview.LocalWorlds.Single(summary => summary.WorldId == "TEST_WORLD_B").State);
        Assert.Equal(["GetConnectionStatus:Dragonwilds", "GetSnapshot:Dragonwilds"], provider.Calls);
    }

    [Fact]
    public async Task RefreshAll_NotConnected_ReportsTheSelectedWorldWithoutReadingTheCloud()
    {
        var selected = await LocalWorldAsync("TEST_WORLD_A", 1);
        provider.IsConnected = false;

        var refresh = await service.RefreshAllAsync(GameId.Dragonwilds, [selected], selected, Token);

        Assert.False(refresh.Overview.IsConnected);
        Assert.Equal(CloudSyncState.NotConnected, refresh.SelectedStatus!.State);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("GetSnapshot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAll_WithoutSelection_HasNoSelectedStatus()
    {
        SeedCloud("TEST_WORLD_A", 1);

        var refresh = await service.RefreshAllAsync(GameId.Dragonwilds, [], null, Token);

        Assert.Null(refresh.SelectedStatus);
        Assert.Single(refresh.Overview.CloudOnlyWorlds);
    }
}
