using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// Playing without SaveHarbor (e.g. starting the game from Steam) must show up as unshared progress.
public sealed class LocalChangeDetectionTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly DragonwildsSaveAdapter adapter;
    private readonly CloudSyncService service;
    private readonly string savePath;

    public LocalChangeDetectionTests()
    {
        var saveRoot = temp.Combine("saves");
        Directory.CreateDirectory(saveRoot);
        savePath = Path.Combine(saveRoot, "TEST_WORLD.sav");
        File.WriteAllBytes(savePath, [1, 2, 3]);
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
        provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
        {
            Game = "dragonwilds",
            WorldId = "TEST_WORLD",
            WorldName = "TEST_WORLD",
            LatestVersion = new CloudVersionMetadata { VersionNumber = 2, VersionId = "v2", UploadedBy = "TEST_USER" }
        });
    }

    public void Dispose() => temp.Dispose();

    private async Task<GameWorld> WorldAsync() => (await adapter.ReadWorldAsync(savePath, TestContext.Current.CancellationToken))!;

    private async Task SyncedAtVersionAsync(int baseVersion, bool withFingerprint = true, DateTimeOffset? uploadedAt = null)
    {
        var world = await WorldAsync();
        var state = await stateService.LoadAsync(world, TestContext.Current.CancellationToken);
        state.LocalBaseVersionNumber = baseVersion;
        state.LastUploadedAtUtc = uploadedAt ?? DateTimeOffset.UtcNow;
        if (withFingerprint)
        {
            await LocalChangeDetector.CaptureAsync(state, world, adapter, TestContext.Current.CancellationToken);
        }

        await stateService.SaveAsync(state, TestContext.Current.CancellationToken);
    }

    private async Task<CloudSyncStatus> StatusAsync() => await service.RefreshStatusAsync(await WorldAsync(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Unchanged_IsInSync()
    {
        await SyncedAtVersionAsync(2);

        Assert.Equal(CloudSyncState.UpToDate, (await StatusAsync()).State);
    }

    [Fact]
    public async Task PlayedWithoutUpload_IsNotSharedYet()
    {
        await SyncedAtVersionAsync(2);
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);

        var status = await StatusAsync();

        Assert.Equal(CloudSyncState.LocalNewerUploadSafe, status.State);
        Assert.True(status.HasLocalChanges);
    }

    [Fact]
    public async Task OnlyTouched_IsStillInSync()
    {
        await SyncedAtVersionAsync(2);
        File.SetLastWriteTimeUtc(savePath, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(CloudSyncState.UpToDate, (await StatusAsync()).State);
    }

    [Fact]
    public async Task PlayedWhileCloudMovedOn_IsConflict()
    {
        await SyncedAtVersionAsync(1);
        File.WriteAllBytes(savePath, [9, 9]);

        var status = await StatusAsync();

        Assert.Equal(CloudSyncState.Conflict, status.State);
        Assert.True(status.HasLocalChanges);
    }

    [Fact]
    public async Task OlderState_WrittenLongAfterUpload_IsNotSharedYet()
    {
        await SyncedAtVersionAsync(2, withFingerprint: false, uploadedAt: DateTimeOffset.UtcNow.AddHours(-2));

        Assert.Equal(CloudSyncState.LocalNewerUploadSafe, (await StatusAsync()).State);
    }

    [Fact]
    public async Task OlderState_Unchanged_GetsAFingerprint()
    {
        File.SetLastWriteTimeUtc(savePath, DateTime.UtcNow.AddHours(-3));
        await SyncedAtVersionAsync(2, withFingerprint: false, uploadedAt: DateTimeOffset.UtcNow.AddHours(-2));

        Assert.Equal(CloudSyncState.UpToDate, (await StatusAsync()).State);
        var state = await stateService.LoadAsync(await WorldAsync(), TestContext.Current.CancellationToken);
        Assert.NotEmpty(state.LocalBaseContentHash);
    }
}
