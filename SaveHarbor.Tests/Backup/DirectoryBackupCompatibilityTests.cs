using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

public sealed class DirectoryBackupCompatibilityTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private async Task<GameWorld> CreateWindroseWorldAsync()
    {
        var worldPath = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", "folder1", "TEST_WORLD_ID", "Test World");
        return (await harness.WindroseAdapter.ReadWorldAsync(worldPath, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Restore_LegacyV1Backup_ReplacesDirectoryPayload()
    {
        var world = await CreateWindroseWorldAsync();
        var zip = ZipManifestEditor.BuildLegacyDirectoryBackup(
            Path.Combine(harness.TempPath, "legacy.zip"), "TEST_WORLD_ID", "restored.txt", "from-backup");

        await harness.Backups.RestoreBackupAsync(zip, world, TestContext.Current.CancellationToken);

        Assert.Equal("from-backup", File.ReadAllText(Path.Combine(world.SavePath, "restored.txt")));
        Assert.False(File.Exists(Path.Combine(world.SavePath, "CURRENT")));
    }

    [Fact]
    public async Task CreateBackup_WindroseWorld_WritesV2DirectoryManifestAndWorldFolder()
    {
        var world = await CreateWindroseWorldAsync();

        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);

        var manifest = await harness.Backups.ReadManifestAsync(backup.FilePath, GameId.Windrose, TestContext.Current.CancellationToken);
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal("Directory", manifest.PayloadKind);
        Assert.Empty(manifest.Files);
        using var archive = System.IO.Compression.ZipFile.OpenRead(backup.FilePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "world/WorldDescription.json");
    }

    [Fact]
    public async Task Restore_WindroseBackupIntoDragonwildsWorld_IsRejectedBeforeAnyChange()
    {
        var windroseWorld = await CreateWindroseWorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(windroseWorld, "manual", TestContext.Current.CancellationToken);
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var before = File.ReadAllBytes(path);
        var dragonwildsWorld = await harness.ReadDragonwildsWorldAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, dragonwildsWorld, TestContext.Current.CancellationToken));

        Assert.Equal(before, File.ReadAllBytes(path));
        var dragonwildsBackups = await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken);
        Assert.Empty(dragonwildsBackups);
    }

    [Fact]
    public async Task Restore_ManifestWithOtherPayloadKind_IsRejected()
    {
        var world = await CreateWindroseWorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        ZipManifestEditor.Update(backup.FilePath, manifest => manifest.PayloadKind = "FileSet");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, world, TestContext.Current.CancellationToken));

        Assert.Equal("This backup was created for a different save layout.", exception.Message);
    }

    [Fact]
    public async Task ReadManifest_UnsupportedSchemaVersion_Throws()
    {
        var world = await CreateWindroseWorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        ZipManifestEditor.Update(backup.FilePath, manifest => manifest.SchemaVersion = 3);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.ReadManifestAsync(backup.FilePath, GameId.Windrose, TestContext.Current.CancellationToken));

        Assert.Equal("Unsupported backup version.", exception.Message);
    }
}
