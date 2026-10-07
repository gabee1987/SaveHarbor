using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// Every world gets its own sync state, and shared worlds missing on this PC are offered for download.
public sealed class CloudWorldOverviewTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly DragonwildsSaveAdapter adapter;
    private readonly CloudSyncService service;
    private readonly string saveRoot;

    public CloudWorldOverviewTests()
    {
        saveRoot = temp.Combine("saves");
        Directory.CreateDirectory(saveRoot);
        adapter = new DragonwildsSaveAdapter(new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Dragonwilds] = new() { SaveRoot = saveRoot }
        }));
        stateService = new LocalJsonSyncStateService(new TestPathProvider(temp));
        service = new CloudSyncService(
            provider,
            stateService,
            new StubBackupService(),
            new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, adapter)),
            new FixedPlayerIdentity(),
            new AppSettingsStore(new TestPathProvider(temp)),
            new NullAppLogger());
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void SeedCloud(string worldId, int version) => provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
    {
        Game = "dragonwilds",
        WorldId = worldId,
        WorldName = worldId,
        LatestVersion = new CloudVersionMetadata { VersionNumber = version, VersionId = $"v{version}", UploadedBy = "TEST_USER", ArchiveFileName = $"TEST_ARCHIVE_v{version}.zip" }
    });

    private async Task<GameWorld> LocalWorldAsync(string worldId, int? baseVersion)
    {
        var path = Path.Combine(saveRoot, $"{worldId}.sav");
        File.WriteAllBytes(path, [1, 2, 3]);
        var world = (await adapter.ReadWorldAsync(path, Token))!;
        if (baseVersion is not null)
        {
            var state = await stateService.LoadAsync(world, Token);
            state.LocalBaseVersionNumber = baseVersion;
            state.LastDownloadedAtUtc = DateTimeOffset.UtcNow;
            await LocalChangeDetector.CaptureAsync(state, world, adapter, Token);
            await stateService.SaveAsync(state, Token);
        }

        return world;
    }

    [Fact]
    public async Task Overview_ClassifiesEachLocalWorldAndListsCloudOnlyWorlds()
    {
        SeedCloud("TEST_WORLD_SYNCED", 3);
        SeedCloud("TEST_WORLD_BEHIND", 5);
        SeedCloud("TEST_WORLD_REMOTE", 2);
        var synced = await LocalWorldAsync("TEST_WORLD_SYNCED", 3);
        var behind = await LocalWorldAsync("TEST_WORLD_BEHIND", 4);
        var localOnly = await LocalWorldAsync("TEST_WORLD_LOCAL", null);

        var overview = await service.GetOverviewAsync(GameId.Dragonwilds, [synced, behind, localOnly], Token);

        Assert.True(overview.IsConnected);
        var states = overview.LocalWorlds.ToDictionary(summary => summary.WorldId, summary => summary.State);
        Assert.Equal(CloudSyncState.UpToDate, states["TEST_WORLD_SYNCED"]);
        Assert.Equal(CloudSyncState.CloudNewer, states["TEST_WORLD_BEHIND"]);
        Assert.Equal(CloudSyncState.ConnectedNoCloudSave, states["TEST_WORLD_LOCAL"]);
        Assert.Equal("TEST_WORLD_REMOTE", Assert.Single(overview.CloudOnlyWorlds).WorldId);
    }

    [Fact]
    public async Task Overview_PlayedSinceDownload_IsUploadNeeded()
    {
        SeedCloud("TEST_WORLD", 2);
        var world = await LocalWorldAsync("TEST_WORLD", 2);
        File.WriteAllBytes(world.SavePath, [9, 9, 9, 9]);

        var overview = await service.GetOverviewAsync(GameId.Dragonwilds, [world], Token);

        var summary = Assert.Single(overview.LocalWorlds);
        Assert.Equal(CloudSyncState.LocalNewerUploadSafe, summary.State);
        Assert.True(summary.HasLocalChanges);
    }

    [Fact]
    public async Task Overview_AgreesWithFullStatus()
    {
        SeedCloud("TEST_WORLD", 4);
        var world = await LocalWorldAsync("TEST_WORLD", 3);
        File.WriteAllBytes(world.SavePath, [7, 7, 7, 7]);

        var overview = await service.GetOverviewAsync(GameId.Dragonwilds, [world], Token);
        var status = await service.RefreshStatusAsync(world, Token);

        Assert.Equal(CloudSyncState.Conflict, status.State);
        Assert.Equal(status.State, Assert.Single(overview.LocalWorlds).State);
    }

    [Fact]
    public async Task Overview_DropsCloudWorldsWithUnsafeIds()
    {
        SeedCloud("..", 1);
        SeedCloud("TEST_WORLD_REMOTE", 1);

        var overview = await service.GetOverviewAsync(GameId.Dragonwilds, [], Token);

        Assert.Equal("TEST_WORLD_REMOTE", Assert.Single(overview.CloudOnlyWorlds).WorldId);
    }

    [Fact]
    public async Task Overview_NotConnected_IsEmpty()
    {
        provider.IsConnected = false;
        SeedCloud("TEST_WORLD_REMOTE", 1);

        var overview = await service.GetOverviewAsync(GameId.Dragonwilds, [], Token);

        Assert.False(overview.IsConnected);
        Assert.Empty(overview.CloudOnlyWorlds);
    }

    [Fact]
    public async Task DownloadCloudWorld_ExistingLocalWorld_IsNeverOverwritten()
    {
        SeedCloud("TEST_WORLD", 2);
        var world = await LocalWorldAsync("TEST_WORLD", null);
        var profile = (await adapter.DiscoverSaveRootsAsync(Token)).Single();

        var result = await service.DownloadCloudWorldAsync(profile, "TEST_WORLD", Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudSyncState.Conflict, result.State);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(world.SavePath));
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("Download:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("TEST_DIR\\TEST_WORLD")]
    public async Task DownloadCloudWorld_UnsafeId_IsRejected(string worldId)
    {
        var profile = (await adapter.DiscoverSaveRootsAsync(Token)).Single();

        var result = await service.DownloadCloudWorldAsync(profile, worldId, Token);

        Assert.False(result.IsSuccess);
        Assert.Empty(provider.Calls);
    }
}
