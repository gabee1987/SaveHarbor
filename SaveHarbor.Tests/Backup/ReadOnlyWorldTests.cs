using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

// A world in a folder SaveHarbor only reads (Enshrouded's Steam Cloud folder) can be backed up, but never replaced or
// removed: Steam would sync the change.
public sealed class ReadOnlyWorldTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restore_ReadOnlyWorld_IsRefusedBeforeAnythingIsWritten()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", Token);
        File.WriteAllBytes(path, [9, 9, 9]);
        var backupsBefore = (await harness.Backups.ListBackupsAsync(world.Game, Token)).Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, world with { IsReadOnly = true }, Token));

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        Assert.Equal(backupsBefore, (await harness.Backups.ListBackupsAsync(world.Game, Token)).Count);
    }

    [Fact]
    public async Task Remove_ReadOnlyWorld_IsRefusedAndTheFileStays()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Backups.RemoveWorldAsync(world with { IsReadOnly = true }, Token));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Backup_ReadOnlyWorld_IsAllowed()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);

        var backup = await harness.Backups.CreateBackupAsync(world with { IsReadOnly = true }, "manual", Token);

        Assert.True(File.Exists(backup.FilePath));
    }
}
