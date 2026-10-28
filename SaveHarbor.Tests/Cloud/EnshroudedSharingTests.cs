using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// Rotating-host sharing of an Enshrouded world between two PCs: two harnesses, each with its own Steam Cloud folder, and
// one fake cloud.
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
        var hostPath = EnshroudedSaveBuilder.Write(first.Harness.EnshroudedRoot, "world3.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var hostWorld = await first.Harness.ReadEnshroudedWorldAsync(hostPath);
        Assert.True((await first.Sync(provider).UploadCurrentAsync(hostWorld, Token)).IsSuccess);
        await ServeLastUploadOfAsync(first, hostWorld);

        var friendProfile = await second.Harness.EnshroudedProfileAsync();
        var downloaded = await second.Sync(provider).DownloadCloudWorldAsync(friendProfile, EnshroudedSaveBuilder.WorldIdOne, Token);

        Assert.True(downloaded.IsSuccess, downloaded.Message);
        var friendPath = Path.Combine(second.Harness.EnshroudedRoot, "world0.ksd");
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
        Assert.Equal(["world3.ksd"], Directory.GetFiles(first.Harness.EnshroudedRoot).Select(Path.GetFileName));
    }
}
