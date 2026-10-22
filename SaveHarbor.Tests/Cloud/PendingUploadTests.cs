using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// A session marks its progress as not shared yet; sharing it, finding nothing to share, or dismissing clears the mark,
// and a failed upload keeps it.
public sealed class PendingUploadTests : IDisposable
{
    private readonly BackupHarness harness = new();
    private readonly FakeCloudProvider provider = new();
    private readonly CloudSyncService service;

    public PendingUploadTests()
    {
        service = new CloudSyncService(
            provider,
            new LocalJsonSyncStateService(harness.Paths, new FixedProviderSelector()),
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

    // A world shared as v1, with a session started on it from this PC.
    private async Task<(GameWorld World, string Path)> PlayingSharedWorldAsync()
    {
        var path = harness.WriteDragonwildsWorld("TEST_WORLD.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        Assert.True((await service.UploadCurrentAsync(world, Token)).IsSuccess);
        Assert.Null(await service.GetPendingUploadAsync(world, Token));
        Assert.True((await service.StartSessionAsync(world, Token)).IsSuccess);
        return (world, path);
    }

    private async Task<GameWorld> PlayAsync(string path)
    {
        File.WriteAllBytes(path, [9, 8, 7, 6]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        return await harness.ReadDragonwildsWorldAsync(path);
    }

    [Fact]
    public async Task StartSession_MarksTheProgressAsNotShared()
    {
        var (world, _) = await PlayingSharedWorldAsync();

        Assert.NotNull(await service.GetPendingUploadAsync(world, Token));
    }

    [Fact]
    public async Task Upload_AfterPlaying_ClearsTheMark()
    {
        var (_, path) = await PlayingSharedWorldAsync();
        var played = await PlayAsync(path);

        Assert.True((await service.UploadCurrentAsync(played, Token)).IsSuccess);

        Assert.Null(await service.GetPendingUploadAsync(played, Token));
    }

    [Fact]
    public async Task Upload_NothingChanged_ClearsTheMark()
    {
        var (world, _) = await PlayingSharedWorldAsync();

        var result = await service.UploadCurrentAsync(world, Token);

        Assert.StartsWith("Nothing changed", result.Message, StringComparison.Ordinal);
        Assert.Null(await service.GetPendingUploadAsync(world, Token));
    }

    [Fact]
    public async Task Upload_Failed_KeepsTheMarkUntilDismissed()
    {
        var (_, path) = await PlayingSharedWorldAsync();
        var played = await PlayAsync(path);
        provider.FailUploadTimes = 1;

        Assert.False((await service.UploadCurrentAsync(played, Token)).IsSuccess);
        Assert.NotNull(await service.GetPendingUploadAsync(played, Token));

        await service.ClearPendingUploadAsync(played, Token);

        Assert.Null(await service.GetPendingUploadAsync(played, Token));
    }
}
