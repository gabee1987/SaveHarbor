using System.IO.Compression;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Backup;

// An Enshrouded backup carries the world as "world.ksd"; restore and import put it in the slot its GUID resolves to on
// this PC, and never into a world with another GUID.
public sealed class EnshroudedPlacementTests : IDisposable
{
    private const string WorldIdThree = "00000000000000000000000000000003";

    private readonly BackupHarness harness = new();

    // A folder of another player's PC, where the shared world came from.
    private string OtherPc => Path.Combine(harness.TempPath, "other-pc");

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<GameWorld> WriteWorldAsync(int slot, string worldId, uint saveCounter = 1) =>
        await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, $"world{slot}.ksd", worldId, saveCounter));

    [Fact]
    public async Task CreateBackup_StoresTheWorldWithoutItsSlot()
    {
        var world = await WriteWorldAsync(7, EnshroudedSaveBuilder.WorldIdOne);

        var backup = await harness.Backups.CreateBackupAsync(world, "manual", Token);

        var manifest = await harness.Backups.ReadManifestAsync(backup.FilePath, GameId.Enshrouded, Token);
        Assert.Equal("world.ksd", Assert.Single(manifest.Files).RelativePath);
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, manifest.WorldId);
        Assert.Equal("2", manifest.SaveFormatVersion);
    }

    [Fact]
    public async Task Restore_SameWorld_PutsTheBytesBackInItsSlot()
    {
        var world = await WriteWorldAsync(7, EnshroudedSaveBuilder.WorldIdOne);
        var original = File.ReadAllBytes(world.SavePath);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", Token);
        File.WriteAllBytes(world.SavePath, EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, saveCounter: 2));

        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);

        Assert.Equal(original, File.ReadAllBytes(world.SavePath));
        Assert.Equal(["world7.ksd"], Directory.GetFiles(harness.EnshroudedRoot).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Restore_BackupOfAnotherWorld_IsRefusedAndTheTargetStays()
    {
        var first = await WriteWorldAsync(0, EnshroudedSaveBuilder.WorldIdOne);
        var second = await WriteWorldAsync(1, EnshroudedSaveBuilder.WorldIdTwo);
        var secondBytes = File.ReadAllBytes(second.SavePath);
        var backup = await harness.Backups.CreateBackupAsync(first, "manual", Token);

        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Backups.RestoreBackupAsync(backup.FilePath, second, Token));

        Assert.Equal(secondBytes, File.ReadAllBytes(second.SavePath));
    }

    [Fact]
    public async Task Import_WorldFromAnotherPcIntoEmptyFolder_LandsInSlotZero()
    {
        var sourcePath = EnshroudedSaveBuilder.Write(OtherPc, "world5.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var source = await harness.ReadEnshroudedWorldAsync(sourcePath);
        var backup = await harness.Backups.CreateBackupAsync(source, "manual", Token);

        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, await harness.EnshroudedProfileAsync(), overwriteExisting: false, Token);

        Assert.Equal(Path.Combine(harness.EnshroudedRoot, "world0.ksd"), imported);
        Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(imported));
    }

    [Fact]
    public async Task Import_NewWorld_TakesTheFirstFreeSlotAndLeavesOtherWorlds()
    {
        var zero = await WriteWorldAsync(0, EnshroudedSaveBuilder.WorldIdOne);
        var one = await WriteWorldAsync(1, EnshroudedSaveBuilder.WorldIdTwo);
        var zeroBytes = File.ReadAllBytes(zero.SavePath);
        var oneBytes = File.ReadAllBytes(one.SavePath);
        var source = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(OtherPc, "world0.ksd", WorldIdThree));
        var backup = await harness.Backups.CreateBackupAsync(source, "manual", Token);

        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, await harness.EnshroudedProfileAsync(), overwriteExisting: false, Token);

        Assert.Equal(Path.Combine(harness.EnshroudedRoot, "world2.ksd"), imported);
        Assert.Equal(zeroBytes, File.ReadAllBytes(zero.SavePath));
        Assert.Equal(oneBytes, File.ReadAllBytes(one.SavePath));
    }

    [Fact]
    public async Task Import_WorldAlreadyInTheFolder_ReusesItsSlotOnlyWhenOverwriteIsConfirmed()
    {
        await WriteWorldAsync(0, EnshroudedSaveBuilder.WorldIdTwo);
        var existing = await WriteWorldAsync(4, EnshroudedSaveBuilder.WorldIdOne, saveCounter: 1);
        var source = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(OtherPc, "world9.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 5));
        var backup = await harness.Backups.CreateBackupAsync(source, "manual", Token);
        var profile = await harness.EnshroudedProfileAsync();

        await Assert.ThrowsAsync<IOException>(() => harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, profile, overwriteExisting: false, Token));
        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, profile, overwriteExisting: true, Token);

        Assert.Equal(existing.SavePath, imported);
        Assert.Equal(5u, EnshroudedSaveHeader.TryRead(imported)!.SaveCounter);
        Assert.Equal(2, Directory.GetFiles(harness.EnshroudedRoot).Length);
    }

    // Backups made while CP2 was being tested stored the slot name ("world1.ksd"). Such a backup may only go back to the
    // file of that name; it must never land in the slot another GUID resolved to.
    [Fact]
    public async Task Import_BackupWithSlotNameForAnotherSlot_IsRefusedAndWritesNothing()
    {
        var source = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(OtherPc, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne));
        var backup = await harness.Backups.CreateBackupAsync(source, "manual", Token);
        RenameWorldEntry(backup.FilePath, "world1.ksd");
        var profile = await harness.EnshroudedProfileAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, profile, overwriteExisting: false, Token));

        Assert.Empty(Directory.Exists(harness.EnshroudedRoot) ? Directory.GetFiles(harness.EnshroudedRoot) : []);
    }

    [Fact]
    public async Task Restore_BackupWithSlotNameOfTheSameFile_StillRestores()
    {
        var world = await WriteWorldAsync(1, EnshroudedSaveBuilder.WorldIdOne);
        var original = File.ReadAllBytes(world.SavePath);
        var backup = await harness.Backups.CreateBackupAsync(world, "manual", Token);
        RenameWorldEntry(backup.FilePath, "world1.ksd");
        File.WriteAllBytes(world.SavePath, EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, saveCounter: 2));

        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);

        Assert.Equal(original, File.ReadAllBytes(world.SavePath));
    }

    private static void RenameWorldEntry(string zipPath, string newName)
    {
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("world/world.ksd")!;
            using var buffer = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(buffer);
            }

            entry.Delete();
            using var target = archive.CreateEntry($"world/{newName}").Open();
            buffer.Position = 0;
            buffer.CopyTo(target);
        }

        ZipManifestEditor.Update(zipPath, manifest => manifest.Files[0].RelativePath = newName);
    }
}
