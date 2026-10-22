using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// A cloud that needs a newer app (SaveHarbor Cloud's minimum version) or shared files written by a newer app (a Google
// Drive manifest or lock with a higher SchemaVersion) block every write, so an old app never corrupts shared state.
public sealed class MinimumVersionGateTests : IDisposable
{
    private const string UpdateText = "This version of SaveHarbor is too old for your group's cloud. Update to continue sharing.";

    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly CloudSyncService service;

    public MinimumVersionGateTests()
    {
        stateService = new LocalJsonSyncStateService(new TestPathProvider(temp), new FixedProviderSelector());
        service = new CloudSyncService(provider, stateService, new StubBackupService(), new StubGameRegistry(), new FixedPlayerIdentity(), new AppSettingsStore(new TestPathProvider(temp)), new NullAppLogger(), new FixedDeviceIdentity(), new CloudProviderOptions());
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GameWorld World { get; } =
        new(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);

    private static CloudWorldManifest Manifest(int schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        Game = GameId.Windrose.ToStorageKey(),
        WorldId = World.WorldId,
        WorldName = World.WorldName,
        LatestVersion = new CloudVersionMetadata { VersionNumber = 2, VersionId = "v2", UploadedBy = "TEST_USER" }
    };

    private void RequireUpdateFromCloud() =>
        provider.ConnectionOverride = new CloudConnectionStatus(false, "SaveHarbor Cloud", "test@example.invalid", "SaveHarbor Cloud needs SaveHarbor 99.0.0 or newer.", Reason: CloudConnectionReason.UpdateRequired);

    private async Task SetLocalBaseAsync(int version)
    {
        var state = await stateService.LoadAsync(World, Token);
        state.LocalBaseVersionNumber = version;
        await stateService.SaveAsync(state, Token);
    }

    private bool WroteToTheCloud() =>
        provider.Calls.Any(call => call.StartsWith("Upload", StringComparison.Ordinal)
            || call.StartsWith("Download", StringComparison.Ordinal)
            || call.StartsWith("TryAcquireSessionLock", StringComparison.Ordinal));

    [Fact]
    public async Task CloudNeedsANewerApp_IsUpdateRequired()
    {
        RequireUpdateFromCloud();

        var status = await service.RefreshStatusAsync(World, Token);

        Assert.Equal(CloudSyncState.UpdateRequired, status.State);
        Assert.Equal("Update required", status.Title);
        Assert.Equal(UpdateText, status.Detail);
    }

    [Fact]
    public async Task CloudNeedsANewerApp_BlocksUploadDownloadAndPlay()
    {
        RequireUpdateFromCloud();

        var upload = await service.UploadCurrentAsync(World, Token);
        var download = await service.DownloadLatestAsync(World, Token);
        var session = await service.StartSessionAsync(World, Token);

        Assert.All([upload, download, session], result =>
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(CloudSyncState.UpdateRequired, result.State);
            Assert.Equal(UpdateText, result.Message);
        });
        Assert.False(WroteToTheCloud());
    }

    [Fact]
    public async Task ManifestFromANewerApp_IsUpdateRequiredAndBlocksWrites()
    {
        provider.SeedManifest(GameId.Windrose, Manifest(CloudSchema.MaxKnownManifestVersion + 1));
        await SetLocalBaseAsync(2);

        var status = await service.RefreshStatusAsync(World, Token);
        var upload = await service.UploadCurrentAsync(World, Token);
        var session = await service.StartSessionAsync(World, Token);

        Assert.Equal(CloudSyncState.UpdateRequired, status.State);
        Assert.Null(status.LatestVersion);
        Assert.False(upload.IsSuccess);
        Assert.False(session.IsSuccess);
        Assert.False(WroteToTheCloud());
    }

    [Fact]
    public async Task LockFromANewerApp_IsUpdateRequired()
    {
        provider.SeedManifest(GameId.Windrose, Manifest(1));
        provider.SeedLock(GameId.Windrose, new CloudSessionLock
        {
            SchemaVersion = CloudSchema.MaxKnownLockVersion + 1,
            WorldId = World.WorldId,
            PlayerName = "TEST_USER",
            MachineName = "TEST_DEVICE",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20)
        });

        var status = await service.RefreshStatusAsync(World, Token);

        Assert.Equal(CloudSyncState.UpdateRequired, status.State);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(CloudSchema.MaxKnownManifestVersion)]
    public async Task KnownManifestVersions_StillSync(int schemaVersion)
    {
        provider.SeedManifest(GameId.Windrose, Manifest(schemaVersion));
        await SetLocalBaseAsync(1);

        var status = await service.RefreshStatusAsync(World, Token);

        Assert.Equal(CloudSyncState.CloudNewer, status.State);
    }

    [Fact]
    public async Task UpdateRequired_AdvisesNoSyncAction()
    {
        RequireUpdateFromCloud();
        var status = await service.RefreshStatusAsync(World, Token);

        var advice = SyncAdvisor.Advise(status);

        Assert.Equal("Update required", advice.Headline);
        Assert.False(advice.HasAction);
        Assert.Equal("Update required", SyncAdvisor.Badge(status.State, hasLocalChanges: false)!.Text);
    }
}
