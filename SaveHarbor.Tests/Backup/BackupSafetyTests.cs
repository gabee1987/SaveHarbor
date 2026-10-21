using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Backup;
using SaveHarbor.App.Utilities;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

public sealed class BackupSafetyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static BackupInfo Backup(string world, string reason, double daysAgo)
    {
        var createdAt = Now.AddDays(-daysAgo);
        var name = BackupFileName.Create(createdAt, world, reason);
        return new BackupInfo(Path.Combine(Path.GetTempPath(), "TEST_BACKUPS", name), name, createdAt, 100);
    }

    [Fact]
    public void BackupFileName_RoundTripsWorldAndReason()
    {
        var name = BackupFileName.Create(Now, "TEST: WORLD", BackupReasons.PreImport, attempt: 3);

        Assert.True(BackupFileName.TryParse(name, out var world, out var reason));
        Assert.Equal("TEST_ WORLD", world);
        Assert.Equal(BackupReasons.PreImport, reason);
        Assert.False(BackupFileName.TryParse("TEST_WORLD.zip", out _, out _));
    }

    [Fact]
    public void Retention_NeverDeletesNewestBackupOfAWorld()
    {
        BackupInfo[] backups =
        [
            Backup("TEST_WORLD_A", BackupReasons.Manual, 1),
            Backup("TEST_WORLD_A", BackupReasons.Manual, 2),
            Backup("TEST_WORLD_A", BackupReasons.Manual, 3),
            Backup("TEST_WORLD_B", BackupReasons.Manual, 40)
        ];

        var deleted = BackupRetention.SelectForDeletion(backups, 1, backups[0].FilePath, Now);

        Assert.Equal(new[] { backups[1], backups[2] }, deleted);
    }

    [Fact]
    public void Retention_KeepsRecentSafetyBackupsAndUnknownFiles()
    {
        var unknown = new BackupInfo(Path.Combine(Path.GetTempPath(), "TEST_BACKUPS", "TEST_FILE.zip"), "TEST_FILE.zip", Now.AddDays(-50), 100);
        BackupInfo[] backups =
        [
            Backup("TEST_WORLD", BackupReasons.Manual, 1),
            Backup("TEST_WORLD", BackupReasons.PreImport, 2),
            Backup("TEST_WORLD", BackupReasons.PreRestore, 30),
            unknown
        ];

        var deleted = BackupRetention.SelectForDeletion(backups, 1, backups[0].FilePath, Now);

        Assert.Equal(new[] { backups[2] }, deleted);
    }

    [Fact]
    public async Task CreateBackup_SameSecondAndReason_KeepsBothBackups()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));

        var first = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, TestContext.Current.CancellationToken);
        var second = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, TestContext.Current.CancellationToken);

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.True(File.Exists(first.FilePath));
        Assert.True(File.Exists(second.FilePath));
    }

    [Fact]
    public async Task ImportOverExistingWorld_BacksUpTheReplacedCopyFirst()
    {
        var path = harness.WriteDragonwildsWorld("TEST_WORLD.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, TestContext.Current.CancellationToken);
        var localProgress = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(path, localProgress);
        var root = (await harness.DragonwildsAdapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken))[0];

        await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, root, overwriteExisting: true, TestContext.Current.CancellationToken);

        var safety = (await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken))
            .Single(item => item.FileName.Contains(BackupReasons.PreImport, StringComparison.Ordinal));
        File.WriteAllBytes(path, [9]);
        await harness.Backups.RestoreBackupAsync(safety.FilePath, world, TestContext.Current.CancellationToken);
        Assert.Equal(localProgress, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task RestoreDirectoryBackup_TamperedPayload_LeavesWorldUntouched()
    {
        var worldPath = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", "folder1", "TEST_WORLD_ID", "Test World");
        var world = (await harness.WindroseAdapter.ReadWorldAsync(worldPath, TestContext.Current.CancellationToken))!;
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, TestContext.Current.CancellationToken);
        ZipManifestEditor.Update(backup.FilePath, manifest => manifest.PayloadSha256 = new string('0', 64));
        var before = Directory.GetFiles(worldPath, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, world, TestContext.Current.CancellationToken));

        Assert.Equal(before, Directory.GetFiles(worldPath, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToArray());
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(worldPath)!, "*.saveharbor-*"));
    }
}
