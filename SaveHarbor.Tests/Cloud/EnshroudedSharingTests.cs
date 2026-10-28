using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// Rotating-host sharing of an Enshrouded world between two PCs (two harnesses with their own save folders, one fake
// cloud), and worlds in the Steam Cloud folder, which SaveHarbor reads but never writes.
public sealed class EnshroudedSharingTests : IDisposable
{
    private readonly FakeCloudProvider provider = new();
    private readonly Pc first = new();
    private readonly Pc second = new();

    public void Dispose()
    {
        first.Dispose();
        second.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Pc : IDisposable
    {
        public Pc()
        {
            States = new LocalJsonSyncStateService(Harness.Paths, new FixedProviderSelector());
        }

        public BackupHarness Harness { get; } = new();

        public LocalJsonSyncStateService States { get; }

        public CloudSyncService Sync(FakeCloudProvider provider) => new(
            provider,
            States,
            Harness.Backups,
            new StubGameRegistry(new StubGameDefinition(GameId.Enshrouded, Harness.EnshroudedAdapter)),
            new FixedPlayerIdentity(),
            Harness.Settings,
            new NullAppLogger(),
            new FixedDeviceIdentity(),
            new CloudProviderOptions());

        public void Dispose() => Harness.Dispose();
    }

    // The fake cloud keeps no archives: it serves the backup the last upload was made from.
    private async Task ServeLastUploadOfAsync(Pc pc, GameWorld world) =>
        provider.ArchiveToServe = (await pc.States.LoadAsync(world, Token)).LastLocalBackupPath;

    private static void SaveProgress(string path, uint saveCounter)
    {
        File.WriteAllBytes(path, EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, saveCounter));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(saveCounter));
    }

    [Fact]
    public async Task RotatingHost_WorldMovesBetweenSlotsOnEachPc()
    {
        var hostPath = EnshroudedSaveBuilder.Write(first.Harness.EnshroudedLocalRoot, "world3.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var hostWorld = await first.Harness.ReadEnshroudedWorldAsync(hostPath);
        Assert.True((await first.Sync(provider).UploadCurrentAsync(hostWorld, Token)).IsSuccess);
        await ServeLastUploadOfAsync(first, hostWorld);

        var friendProfile = await second.Harness.EnshroudedLocalProfileAsync();
        var downloaded = await second.Sync(provider).DownloadCloudWorldAsync(friendProfile, EnshroudedSaveBuilder.WorldIdOne, Token);

        Assert.True(downloaded.IsSuccess, downloaded.Message);
        var friendPath = Path.Combine(second.Harness.EnshroudedLocalRoot, "world0.ksd");
        Assert.Equal(File.ReadAllBytes(hostPath), File.ReadAllBytes(friendPath));

        SaveProgress(friendPath, saveCounter: 2);
        var friendWorld = await second.Harness.ReadEnshroudedWorldAsync(friendPath);
        var shared = await second.Sync(provider).UploadCurrentAsync(friendWorld, Token);
        Assert.True(shared.IsSuccess, shared.Message);
        await ServeLastUploadOfAsync(second, friendWorld);

        var back = await first.Sync(provider).DownloadLatestAsync(hostWorld, Token);

        Assert.True(back.IsSuccess, back.Message);
        Assert.Equal(File.ReadAllBytes(friendPath), File.ReadAllBytes(hostPath));
        Assert.Equal(2u, EnshroudedSaveHeader.TryRead(hostPath)!.SaveCounter);
        Assert.Equal(["world3.ksd"], Directory.GetFiles(first.Harness.EnshroudedLocalRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task SteamWorld_CanBeSharedWhileItHasNoLocalCopy()
    {
        var steamWorld = await first.Harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(first.Harness.EnshroudedSteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne));

        var result = await first.Sync(provider).UploadCurrentAsync(steamWorld, Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(provider.StoredVersions);
    }

    [Fact]
    public async Task SteamWorld_DownloadGoesToANewLocalSlotAndLeavesTheSteamFile()
    {
        var steamPath = EnshroudedSaveBuilder.Write(first.Harness.EnshroudedSteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var steamBytes = File.ReadAllBytes(steamPath);
        var steamWorld = await first.Harness.ReadEnshroudedWorldAsync(steamPath);
        Assert.True((await first.Sync(provider).UploadCurrentAsync(steamWorld, Token)).IsSuccess);
        await ServeLastUploadOfAsync(first, steamWorld);

        var result = await first.Sync(provider).DownloadLatestAsync(steamWorld, Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(steamBytes, File.ReadAllBytes(steamPath));
        Assert.Equal(steamBytes, File.ReadAllBytes(Path.Combine(first.Harness.EnshroudedLocalRoot, "world0.ksd")));
    }

    [Fact]
    public async Task SteamWorld_DownloadReplacesItsLocalCopyInstead()
    {
        var steamPath = EnshroudedSaveBuilder.Write(first.Harness.EnshroudedSteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var steamBytes = File.ReadAllBytes(steamPath);
        var localPath = Path.Combine(first.Harness.EnshroudedLocalRoot, "world6.ksd");
        Directory.CreateDirectory(first.Harness.EnshroudedLocalRoot);
        SaveProgress(localPath, saveCounter: 4);
        var localWorld = await first.Harness.ReadEnshroudedWorldAsync(localPath);
        Assert.True((await first.Sync(provider).UploadCurrentAsync(localWorld, Token)).IsSuccess);
        await ServeLastUploadOfAsync(first, localWorld);
        var uploadedBytes = File.ReadAllBytes(localPath);
        SaveProgress(localPath, saveCounter: 5);

        var result = await first.Sync(provider).DownloadLatestAsync(await first.Harness.ReadEnshroudedWorldAsync(steamPath), Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(uploadedBytes, File.ReadAllBytes(localPath));
        Assert.Equal(steamBytes, File.ReadAllBytes(steamPath));
        Assert.Equal(["world6.ksd"], Directory.GetFiles(first.Harness.EnshroudedLocalRoot).Select(Path.GetFileName));
    }

    // Both copies share one sync state; sharing the older Steam copy would replace the progress made in the Local one.
    [Fact]
    public async Task SteamWorld_WithALocalCopy_CannotStartASessionOrBeShared()
    {
        var steamWorld = await first.Harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(first.Harness.EnshroudedSteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne));
        var localWorld = await first.Harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(first.Harness.EnshroudedLocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 2));
        Assert.True((await first.Sync(provider).UploadCurrentAsync(localWorld, Token)).IsSuccess);

        var session = await first.Sync(provider).StartSessionAsync(steamWorld, Token);
        var upload = await first.Sync(provider).UploadCurrentAsync(steamWorld, Token);

        Assert.False(session.IsSuccess);
        Assert.False(upload.IsSuccess);
        Assert.Contains("Select that copy", upload.Message, StringComparison.Ordinal);
        Assert.Single(provider.StoredVersions);
    }
}
