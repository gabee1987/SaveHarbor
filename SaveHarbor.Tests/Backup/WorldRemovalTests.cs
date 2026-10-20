using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Backup;
using SaveHarbor.App.Infrastructure.Migrations;
using SaveHarbor.App.Utilities;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

// Removing a world, locally or from the cloud, must always leave a verified backup behind.
public sealed class WorldRemovalTests : IDisposable
{
    private readonly BackupHarness harness = new();
    private readonly FakeCloudProvider provider = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CloudSyncService CreateSyncService() => new(
        provider,
        new LocalJsonSyncStateService(harness.Paths, new FixedProviderSelector()),
        harness.Backups,
        new StubGameRegistry(new StubGameDefinition(GameId.Dragonwilds, harness.DragonwildsAdapter)),
        new FixedPlayerIdentity(),
        harness.Settings,
        new NullAppLogger(),
        new FixedDeviceIdentity(),
        new CloudProviderOptions());

    [Fact]
    public async Task Backups_AreStoredInTheWorldsOwnFolder()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));

        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);

        Assert.Equal(harness.Backups.GetWorldBackupFolder(GameId.Dragonwilds, "TEST_WORLD"), Path.GetDirectoryName(backup.FilePath));
        Assert.Single(await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, Token));
    }

    [Fact]
    public async Task Organizer_MovesOldRootBackupsIntoWorldFolders_AndLeavesUnknownFiles()
    {
        var root = harness.Backups.GetBackupRoot(GameId.Dragonwilds);
        Directory.CreateDirectory(root);
        var oldName = BackupFileName.Create(DateTimeOffset.UtcNow, "TEST_WORLD", BackupReasons.Manual);
        File.WriteAllBytes(Path.Combine(root, oldName), [1]);
        File.WriteAllBytes(Path.Combine(root, "TEST_FILE.zip"), [2]);

        var report = new BackupFolderOrganizer(harness.Paths, new NullAppLogger()).Run([GameId.Dragonwilds]);

        Assert.Equal(1, report.MovedItems);
        Assert.True(File.Exists(Path.Combine(root, "TEST_WORLD", oldName)));
        Assert.True(File.Exists(Path.Combine(root, "TEST_FILE.zip")));
        Assert.Equal(2, (await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, Token)).Count);
    }

    [Fact]
    public async Task RemoveWorld_BacksUpThenDeletes_AndKeepsTheGamesOwnCopy()
    {
        var path = harness.WriteDragonwildsWorld("TEST_WORLD.sav");
        var original = File.ReadAllBytes(path);
        File.WriteAllBytes(path + ".backup", [7, 7, 7]);
        var world = await harness.ReadDragonwildsWorldAsync(path);

        var backup = await harness.Backups.RemoveWorldAsync(world, Token);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".backup"));
        Assert.True(BackupFileName.TryParse(backup.FileName, out _, out var reason));
        Assert.Equal(BackupReasons.PreRemove, reason);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(backup.FilePath)!, "*.sav.backup"));

        var profile = (await harness.DragonwildsAdapter.DiscoverSaveRootsAsync(Token)).Single();
        var restored = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, profile, overwriteExisting: false, Token);
        Assert.Equal(original, File.ReadAllBytes(restored));
    }

    [Fact]
    public async Task RemoveWorld_MissingSave_ChangesNothing()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
        File.Delete(world.SavePath);

        await Assert.ThrowsAsync<FileNotFoundException>(() => harness.Backups.RemoveWorldAsync(world, Token));
        Assert.Empty(await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, Token));
    }

    [Fact]
    public void Retention_NeverDeletesRemovalBackups()
    {
        var now = DateTimeOffset.UtcNow;
        BackupInfo Make(string reason, int daysAgo)
        {
            var name = BackupFileName.Create(now.AddDays(-daysAgo), "TEST_WORLD", reason);
            return new BackupInfo(Path.Combine(harness.TempPath, name), name, now.AddDays(-daysAgo), 1);
        }

        BackupInfo[] backups = [Make(BackupReasons.Manual, 1), Make(BackupReasons.PreRemove, 90), Make(BackupReasons.CloudRemoved, 91)];

        Assert.Empty(BackupRetention.SelectForDeletion(backups, 1, backups[0].FilePath, now));
    }

    private async Task<string> SeedCloudWorldAsync()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));
        var archive = await harness.Backups.CreateBackupAsync(world, BackupReasons.CloudUpload, Token);
        var served = Path.Combine(harness.TempPath, "TEST_SERVED.zip");
        File.Move(archive.FilePath, served);
        provider.ArchiveToServe = served;
        provider.SeedManifest(GameId.Dragonwilds, new CloudWorldManifest
        {
            Game = "dragonwilds",
            WorldId = "TEST_WORLD",
            WorldName = "TEST_WORLD",
            LatestVersion = new CloudVersionMetadata
            {
                VersionNumber = 3,
                VersionId = "v3",
                UploadedBy = "TEST_USER",
                ArchiveFileName = "TEST_ARCHIVE_v3.zip",
                ArchiveSha256 = await FileHashCalculator.ComputeSha256Async(served, Token)
            }
        });
        return served;
    }

    [Fact]
    public async Task RemoveCloudWorld_KeepsLatestVersionAsBackupFirst()
    {
        await SeedCloudWorldAsync();

        var result = await CreateSyncService().RemoveCloudWorldAsync(GameId.Dragonwilds, "TEST_WORLD", Token);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Contains("RemoveWorld:Dragonwilds:TEST_WORLD", provider.Calls);
        var kept = Assert.Single(await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, Token));
        Assert.True(BackupFileName.TryParse(kept.FileName, out _, out var reason));
        Assert.Equal(BackupReasons.CloudRemoved, reason);
    }

    [Fact]
    public async Task RemoveCloudWorld_BackupNotPossible_RemovesNothing()
    {
        await SeedCloudWorldAsync();
        provider.ArchiveToServe = null;

        var result = await CreateSyncService().RemoveCloudWorldAsync(GameId.Dragonwilds, "TEST_WORLD", Token);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("RemoveWorld:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveCloudWorld_TamperedArchive_RemovesNothing()
    {
        var served = await SeedCloudWorldAsync();
        File.AppendAllText(served, "TEST_SECRET_VALUE");

        var result = await CreateSyncService().RemoveCloudWorldAsync(GameId.Dragonwilds, "TEST_WORLD", Token);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("RemoveWorld:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveCloudWorld_WhileSomeoneElsePlays_IsBlocked()
    {
        await SeedCloudWorldAsync();
        provider.SeedLock(GameId.Dragonwilds, new CloudSessionLock
        {
            WorldId = "TEST_WORLD",
            PlayerName = "TEST_USER",
            MachineName = "TEST_OTHER_MACHINE",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var result = await CreateSyncService().RemoveCloudWorldAsync(GameId.Dragonwilds, "TEST_WORLD", Token);

        Assert.Equal(CloudSyncState.SomeonePlaying, result.State);
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("Download:", StringComparison.Ordinal));
    }
}
