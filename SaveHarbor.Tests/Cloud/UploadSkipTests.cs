using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// An upload of a world that still matches its cloud version adds no version; one with progress does.
public sealed class UploadSkipTests : IDisposable
{
    private readonly BackupHarness harness = new();
    private readonly FakeCloudProvider provider = new();
    private readonly LocalJsonSyncStateService stateService;
    private readonly CloudSyncService service;

    public UploadSkipTests()
    {
        stateService = new LocalJsonSyncStateService(harness.Paths, new FixedProviderSelector());
        service = new CloudSyncService(
            provider,
            stateService,
            harness.Backups,
            new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, harness.DragonwildsAdapter)),
            new FixedPlayerIdentity(),
            harness.Settings,
            new NullAppLogger(),
            new FixedDeviceIdentity(),
            new CloudProviderOptions());
    }

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_UnchangedSinceLastUpload_AddsNoVersion()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
        Assert.True((await service.UploadCurrentAsync(world, Token)).IsSuccess);

        var result = await service.UploadCurrentAsync(world, Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(CloudSyncState.UpToDate, result.State);
        Assert.Equal("Nothing changed since v1; nothing to upload.", result.Message);
        Assert.Single(provider.StoredVersions);
    }

    [Fact]
    public async Task Upload_AfterProgress_AddsTheNextVersion()
    {
        var path = harness.WriteDragonwildsWorld("TEST_WORLD.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        Assert.True((await service.UploadCurrentAsync(world, Token)).IsSuccess);

        File.WriteAllBytes(path, [9, 8, 7, 6]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var result = await service.UploadCurrentAsync(await harness.ReadDragonwildsWorldAsync(path), Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, provider.StoredVersions.Count);
        Assert.Contains("v2", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_WithoutFingerprint_Uploads()
    {
        provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
        {
            Game = "dragonwilds",
            WorldId = "TEST_WORLD",
            WorldName = "TEST_WORLD",
            LatestVersion = new CloudVersionMetadata { VersionNumber = 3, VersionId = "v3", UploadedBy = "TEST_USER" }
        });
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
        var state = await stateService.LoadAsync(world, Token);
        state.LocalBaseVersionNumber = 3;
        await stateService.SaveAsync(state, Token);

        var result = await service.UploadCurrentAsync(world, Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(provider.StoredVersions);
    }
}
