using System.Security.Cryptography;
using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

public sealed class FileSetBackupTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private string[] Leftovers() => Directory.GetFiles(harness.DragonwildsRoot, "*.saveharbor-*", SearchOption.AllDirectories);

    [Fact]
    public async Task Restore_ModifiedOriginal_RestoresBackedUpBytes()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var characterFile = Path.Combine(harness.DragonwildsRoot, "SaveCharacters", "char.json");
        Directory.CreateDirectory(Path.GetDirectoryName(characterFile)!);
        File.WriteAllText(characterFile, "{\"name\":\"TEST_USER\"}");
        var characterHash = Hash(characterFile);
        var originalHash = Hash(path);
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));

        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, TestContext.Current.CancellationToken);

        Assert.Equal(originalHash, Hash(path));
        Assert.Equal("My World.sav", Path.GetFileName(path));
        Assert.True((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < 5);
        Assert.Empty(Leftovers());
        Assert.Equal(characterHash, Hash(characterFile));
    }

    [Fact]
    public async Task Restore_WrongEntryHash_ThrowsAndLeavesOriginalUntouched()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        ZipManifestEditor.Update(backup.FilePath, manifest => manifest.Files[0].Sha256 = new string('0', 64));
        File.WriteAllBytes(path, [9, 9, 9]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, world, TestContext.Current.CancellationToken));

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task Restore_TargetLocked_ThrowsAndLeavesNoTemporaryFiles()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        File.WriteAllBytes(path, [7, 7, 7]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                harness.Backups.RestoreBackupAsync(backup.FilePath, world, TestContext.Current.CancellationToken));
        }

        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(path));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task Restore_BackupOfDifferentFileName_IsRejected()
    {
        var first = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("First World.sav"));
        var second = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("Second World.sav"));
        var backup = await harness.Backups.CreateBackupAsync(first, "manual", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.RestoreBackupAsync(backup.FilePath, second, TestContext.Current.CancellationToken));

        Assert.Equal("This backup belongs to a world file with a different name.", exception.Message);
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task Import_MissingWorld_CreatesFileAndReturnsItsPath()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var originalHash = Hash(path);
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        File.Delete(path);
        var root = (await harness.DragonwildsAdapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken)).Single();

        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, root, overwriteExisting: false, TestContext.Current.CancellationToken);

        Assert.Equal(path, imported, ignoreCase: true);
        Assert.Equal(originalHash, Hash(imported));
    }

    [Fact]
    public async Task Import_ExistingWorldWithoutOverwrite_Throws()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);
        var root = (await harness.DragonwildsAdapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken)).Single();

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, root, overwriteExisting: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateBackup_FileSetWorld_WritesV2ManifestWithFileEntry()
    {
        var path = harness.WriteDragonwildsWorld("My World.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);

        var backup = await harness.Backups.CreateBackupAsync(world, "manual", TestContext.Current.CancellationToken);

        var manifest = await harness.Backups.ReadManifestAsync(backup.FilePath, GameId.Dragonwilds, TestContext.Current.CancellationToken);
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal("FileSet", manifest.PayloadKind);
        var entry = Assert.Single(manifest.Files);
        Assert.Equal("My World.sav", entry.RelativePath);
        Assert.Equal(Hash(path), entry.Sha256);
    }
}
