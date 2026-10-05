using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

public sealed class CloudVersionRetentionTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CloudStoredVersion Version(int number) => new($"20260101_000000_TEST_USER_v{number}.zip");

    [Fact]
    public void SelectForRemoval_KeepsNewestLatestProtectedAndUnnumbered()
    {
        CloudStoredVersion[] stored = [Version(1), Version(2), Version(3), Version(4), Version(5), new("TEST_FILE.zip")];

        var removed = CloudVersionRetention.SelectForRemoval(stored, keepCount: 2, latestVersionNumber: 5, protectedVersionNumbers: new HashSet<int> { 2 });

        Assert.Equal(new[] { 3, 1 }, removed.Select(version => version.VersionNumber!.Value));
    }

    [Fact]
    public void SelectForRemoval_ZeroKeepsEverything()
    {
        Assert.Empty(CloudVersionRetention.SelectForRemoval([Version(1), Version(2)], 0, 2, new HashSet<int>()));
    }

    [Fact]
    public async Task Upload_WithLimit_RemovesOlderSharedVersions()
    {
        var provider = new FakeCloudProvider();
        provider.StoredVersions.AddRange(Enumerable.Range(1, 4).Select(number => Version(number).ArchiveFileName));
        provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
        {
            Game = "dragonwilds",
            WorldId = "TEST_WORLD",
            WorldName = "TEST_WORLD",
            LatestVersion = new CloudVersionMetadata { VersionNumber = 4, VersionId = "v4", UploadedBy = "TEST_USER" }
        });
        harness.Settings.Update(settings => settings.CloudVersionRetentionCount = 2);
        var stateService = new LocalJsonSyncStateService(harness.Paths);
        var service = new CloudSyncService(
            provider,
            stateService,
            harness.Backups,
            new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, harness.DragonwildsAdapter)),
            new FixedPlayerIdentity(),
            harness.Settings,
            new NullAppLogger());
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
        var state = await stateService.LoadAsync(world, TestContext.Current.CancellationToken);
        state.LocalBaseVersionNumber = 4;
        await stateService.SaveAsync(state, TestContext.Current.CancellationToken);

        var result = await service.UploadCurrentAsync(world, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(new int?[] { 4, 5 }, provider.StoredVersions.Select(name => new CloudStoredVersion(name).VersionNumber).Order());
    }
}
