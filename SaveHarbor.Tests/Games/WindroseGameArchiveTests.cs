using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// Windrose only lists a world that also has its own archive in RocksDB_v2_Backups, so the archive must travel with
// every backup and come back on restore and import (docs/plans/windrose/00-overview.md finding F8).
public sealed class WindroseGameArchiveTests : IDisposable
{
    private const string WorldId = "TEST_WORLD_ID";

    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ArchivePath(string profileId) =>
        Path.Combine(harness.WindroseRoot, profileId, "RocksDB_v2_Backups", "Worlds", WorldId, $"{WorldId}_0.10.0_Latest.zip");

    private async Task<GameWorld> WorldAsync(bool withArchive = true, string? extraEntry = null, string archiveIslandId = WorldId)
    {
        var path = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", WorldId, WorldId, "TEST_WORLD");
        if (withArchive)
        {
            SaveFixtures.CreateWindroseGameArchive(path, archiveIslandId, extraEntry);
        }

        return (await harness.WindroseAdapter.ReadWorldAsync(path, Token))!;
    }

    // A second profile stands in for a friend's PC that has never seen the world.
    private async Task<GameSaveRoot> OtherPcAsync()
    {
        Directory.CreateDirectory(Path.Combine(harness.WindroseRoot, "67890", "RocksDB_v2", "0.10.0", "Worlds"));
        return (await harness.WindroseAdapter.DiscoverSaveRootsAsync(Token)).Single(root => root.RootId == "67890");
    }

    private IEnumerable<SaveHealthIssue> ArchiveIssues(GameWorld world) =>
        harness.WindroseAdapter.CheckHealth(world)!.Select(notice => notice.Issue).Where(issue => issue is SaveHealthIssue.GameArchiveMissing);

    [Fact]
    public async Task ImportOnAnotherPc_PutsTheGameArchiveWhereTheGameLoadsIt()
    {
        var backup = await harness.Backups.CreateBackupAsync(await WorldAsync(), BackupReasons.CloudUpload, Token);

        var importedPath = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, await OtherPcAsync(), overwriteExisting: false, Token);

        Assert.True(File.Exists(ArchivePath("67890")));
        Assert.True(File.GetLastWriteTimeUtc(ArchivePath("67890")) > DateTime.UtcNow.AddMinutes(-5)); // newest for Steam Cloud
        Assert.False(Directory.Exists(Path.Combine(importedPath, BackupPayloadLayout.GameFilesFolderName)));
        var imported = (await harness.WindroseAdapter.ReadWorldAsync(importedPath, Token))!;
        Assert.Empty(ArchiveIssues(imported));
    }

    // The one-PC test: remove the world, then bring it back from its backup as a download would.
    [Fact]
    public async Task RemovedWorld_LeavesNothingTheGameCouldLoad_AndComesBackWithItsArchive()
    {
        var world = await WorldAsync();

        var backup = await harness.Backups.RemoveWorldAsync(world, Token);

        Assert.False(Directory.Exists(world.SavePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(ArchivePath("12345"))));
        Assert.Single(Directory.GetFiles(Path.Combine(harness.Paths.AppDataRoot, "removed", "windrose"), "*_Latest.zip", SearchOption.AllDirectories));

        var profile = (await harness.WindroseAdapter.DiscoverSaveRootsAsync(Token)).Single(root => root.RootId == "12345");
        await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, profile, overwriteExisting: false, Token);

        Assert.True(File.Exists(ArchivePath("12345")));
        Assert.Empty(ArchiveIssues(world));
    }

    [Fact]
    public async Task Restore_PutsTheGameArchiveBack()
    {
        var world = await WorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);
        File.Delete(ArchivePath("12345"));

        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);

        Assert.True(File.Exists(ArchivePath("12345")));
        Assert.Empty(ArchiveIssues(world));
    }

    [Theory]
    [InlineData("Checkpoint/../../TEST_ESCAPE.txt", WorldId)]
    [InlineData("TEST_OTHER/file.txt", WorldId)]
    [InlineData(null, "TEST_OTHER_WORLD_ID")]
    public async Task UntrustedArchive_IsRefused_AndNothingIsWritten(string? extraEntry, string archiveIslandId)
    {
        var backup = await harness.Backups.CreateBackupAsync(await WorldAsync(extraEntry: extraEntry, archiveIslandId: archiveIslandId), BackupReasons.CloudUpload, Token);
        var otherPc = await OtherPcAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, otherPc, overwriteExisting: false, Token));

        Assert.False(Directory.Exists(Path.Combine(otherPc.WorldsPath, WorldId)));
        Assert.False(File.Exists(ArchivePath("67890")));
    }

    [Fact]
    public async Task WorldWithoutGameArchive_IsReported()
    {
        var world = await WorldAsync(withArchive: false);

        Assert.Equal([SaveHealthIssue.GameArchiveMissing], ArchiveIssues(world));
    }

    // On exit the game writes its archive and then compacts the database, so the folder no longer matches the archive.
    // The game loads the archive, so the backup must hold the folder rebuilt from it, not the compacted folder.
    [Fact]
    public async Task Backup_HoldsTheStateTheGameLoads_NotTheCompactedFolder()
    {
        // Newer game archives also carry a second copy of the description under Checkpoint/.
        var world = await WorldAsync(extraEntry: "Checkpoint/AdditionalRecordFiles/WorldDescription.json");
        File.Delete(Path.Combine(world.SavePath, "000001.sst"));
        File.WriteAllText(Path.Combine(world.SavePath, "000009.sst"), "TEST_COMPACTED_TABLE");
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.CloudUpload, Token);

        var importedPath = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, await OtherPcAsync(), overwriteExisting: false, Token);

        Assert.Equal("TEST_TABLE", File.ReadAllText(Path.Combine(importedPath, "000001.sst")));
        Assert.False(File.Exists(Path.Combine(importedPath, "000009.sst")));
        Assert.True(File.Exists(Path.Combine(importedPath, "MANIFEST-000001")));
        Assert.True(File.Exists(Path.Combine(importedPath, "CURRENT")));
        var imported = (await harness.WindroseAdapter.ReadWorldAsync(importedPath, Token))!;
        Assert.Empty(harness.WindroseAdapter.CheckHealth(imported)!);
    }

    // An archive that is not a complete checkpoint still travels, but the world folder is backed up as it is.
    [Fact]
    public async Task IncompleteGameArchive_KeepsTheWorldFolderAsItIs()
    {
        var world = await WorldAsync(extraEntry: "Checkpoint/shared_checksum/TEST_UNKNOWN_NAME.sst");
        File.WriteAllText(Path.Combine(world.SavePath, "000009.sst"), "TEST_FOLDER_TABLE");

        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);
        File.Delete(Path.Combine(world.SavePath, "000009.sst"));
        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);

        Assert.Equal("TEST_FOLDER_TABLE", File.ReadAllText(Path.Combine(world.SavePath, "000009.sst")));
    }
}
