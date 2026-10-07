using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// RocksDB never reuses a file number, so after SaveHarbor writes a world a lower-numbered file it did not write means
// the folder was swapped (docs/plans/windrose/00-overview.md finding F2).
public sealed class WindroseSaveHealthTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<GameWorld> RestoredWorldAsync()
    {
        var path = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", "TEST_WORLD_ID", "TEST_WORLD_ID", "TEST_WORLD");
        File.WriteAllText(Path.Combine(path, "000007.sst"), "TEST_TABLE");
        var world = (await harness.WindroseAdapter.ReadWorldAsync(path, Token))!;
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);
        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);
        return world;
    }

    private IReadOnlyList<SaveHealthNotice> Health(GameWorld world) => harness.WindroseAdapter.CheckHealth(world)!;

    [Fact]
    public async Task WorldNeverWrittenBySaveHarbor_HasNoProblems()
    {
        var path = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", "TEST_WORLD_ID", "TEST_WORLD_ID", "TEST_WORLD");
        var world = (await harness.WindroseAdapter.ReadWorldAsync(path, Token))!;

        Assert.Empty(Health(world));
    }

    [Fact]
    public async Task NormalPlayAfterRestore_IsNotReportedAsReplaced()
    {
        var world = await RestoredWorldAsync();

        // Playing adds higher numbers, grows the manifest and compacts old tables away.
        File.WriteAllText(Path.Combine(world.SavePath, "000012.sst"), "TEST_NEW_TABLE");
        File.WriteAllText(Path.Combine(world.SavePath, "MANIFEST-000013"), "TEST_MANIFEST");
        File.AppendAllText(Path.Combine(world.SavePath, "MANIFEST-000001"), "TEST_GROWTH");
        File.Delete(Path.Combine(world.SavePath, "000007.sst"));

        Assert.Empty(Health(world));
    }

    [Fact]
    public async Task FolderSwappedAfterRestore_IsReported_UntilKeptAsItIs()
    {
        var world = await RestoredWorldAsync();

        File.WriteAllText(Path.Combine(world.SavePath, "000005.sst"), "TEST_OLDER_TABLE");

        var notice = Assert.Single(Health(world));
        Assert.Equal(SaveHealthIssue.WorldReplaced, notice.Issue);

        harness.WindroseAdapter.RememberWorldState(world.SavePath);
        Assert.Empty(Health(world));
    }

    [Fact]
    public async Task ChangedTableWithKnownNumber_IsReported()
    {
        var world = await RestoredWorldAsync();

        File.WriteAllText(Path.Combine(world.SavePath, "000007.sst"), "TEST_DIFFERENT_LENGTH_TABLE");

        Assert.Contains(Health(world), notice => notice.Issue == SaveHealthIssue.WorldReplaced);
    }

    [Fact]
    public async Task UnreadableRecord_IsIgnored()
    {
        var world = await RestoredWorldAsync();
        File.WriteAllText(Path.Combine(world.SavePath, "000005.sst"), "TEST_OLDER_TABLE");
        foreach (var record in Directory.GetFiles(harness.Paths.GetSyncStateRoot(GameId.Windrose), "*.json", SearchOption.AllDirectories))
        {
            File.WriteAllText(record, "{ not json");
        }

        Assert.Empty(Health(world));
    }

    [Fact]
    public async Task Leftovers_AreReported_AndMovedOutOfWorlds()
    {
        var world = await RestoredWorldAsync();
        var leftover = world.SavePath + ".saveharbor-prev-TEST";
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "000001.sst"), "TEST_TABLE");

        Assert.Contains(Health(world), notice => notice.Issue == SaveHealthIssue.Leftovers);

        var moved = Assert.Single(harness.WindroseAdapter.MoveLeftoversAside(world));

        Assert.False(Directory.Exists(leftover));
        Assert.True(File.Exists(Path.Combine(moved, "000001.sst")));
        Assert.StartsWith(harness.Paths.AppDataRoot, moved, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Health(world));
    }
}
