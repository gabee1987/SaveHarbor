using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class EnshroudedSlotMapTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string Write(string fileName, string worldId) => EnshroudedSaveBuilder.Write(temp.Path, fileName, worldId);

    [Fact]
    public void ResolveTargetPath_SameWorldInAnySlot_ReusesThatFile()
    {
        Write("world0.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var existing = Write("world7.ksd", EnshroudedSaveBuilder.WorldIdOne);

        Assert.Equal(existing, EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_EmptyOrMissingFolder_UsesSlotZero()
    {
        var missing = temp.Combine("not-created-yet");

        Assert.Equal(Path.Combine(temp.Path, "world0.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
        Assert.Equal(Path.Combine(missing, "world0.ksd"), EnshroudedSlotMap.ResolveTargetPath(missing, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_NewWorld_FillsTheFirstGap()
    {
        Write("world0.ksd", "00000000000000000000000000000010");
        Write("world1.ksd", "00000000000000000000000000000011");
        Write("world3.ksd", "00000000000000000000000000000013");

        Assert.Equal(Path.Combine(temp.Path, "world2.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_SlotOfAWorldDeletedInTheGame_IsNotReused()
    {
        // Deleting a world in the game leaves its rolling copies; Steam then hides a new file of that name from the game.
        Write("world0.ksd", "00000000000000000000000000000010");
        Write(Path.Combine("backup", "world0.ksd.backup-0"), "00000000000000000000000000000010");
        Write(Path.Combine("backup", "world1.ksd.backup-0"), "00000000000000000000000000000011");
        Write(Path.Combine("backup", "world2.ksd.backup-9"), "00000000000000000000000000000012");

        Assert.Equal(Path.Combine(temp.Path, "world3.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_SlotSaveHarborRemovedAWorldFrom_IsNotReused()
    {
        Directory.CreateDirectory(temp.Combine(EnshroudedSlotMap.AsideFolderName, "20260101-120000_world0"));
        Directory.CreateDirectory(temp.Combine(EnshroudedSlotMap.AsideFolderName, "TEST_FOLDER_world1"));

        Assert.Equal(Path.Combine(temp.Path, "world1.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_WorldWithRollingCopies_IsStillReused()
    {
        var existing = Write("world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        Write(Path.Combine("backup", "world1.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdOne);

        Assert.Equal(existing, EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_OtherRollingCopies_DoNotTakeASlot()
    {
        Write(Path.Combine("backup", "character0.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdTwo);
        Write(Path.Combine("backup", "global.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdTwo);
        Write(Path.Combine("backup", "world0.ksd_old"), EnshroudedSaveBuilder.WorldIdTwo);

        Assert.Equal(Path.Combine(temp.Path, "world0.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_UnreadableWorldFile_IsNeitherReusedNorOverwritten()
    {
        File.WriteAllText(temp.Combine("world0.ksd"), "TEST_CONTENT");

        Assert.Equal(Path.Combine(temp.Path, "world1.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void ResolveTargetPath_CharacterFileWithTheSameGuid_IsIgnored()
    {
        Write("character0.ksd", EnshroudedSaveBuilder.WorldIdOne);

        Assert.Equal(Path.Combine(temp.Path, "world0.ksd"), EnshroudedSlotMap.ResolveTargetPath(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Theory]
    [InlineData("world0.ksd", 0)]
    [InlineData("World12.KSD", 12)]
    [InlineData("world9999.ksd", 9999)]
    public void TryGetSlot_WorldFileName_ReturnsSlot(string fileName, int expected)
    {
        Assert.True(EnshroudedSlotMap.TryGetSlot(fileName, out var slot));
        Assert.Equal(expected, slot);
    }

    [Theory]
    [InlineData("character0.ksd")]
    [InlineData("global.ksd")]
    [InlineData("worldX.ksd")]
    [InlineData("world.ksd")]
    [InlineData("world1.ksd.backup-3")]
    [InlineData("world5.ksd_backup")]
    [InlineData("world10000.ksd")]
    [InlineData("world-1.ksd")]
    [InlineData("world1.ksdx")]
    public void TryGetSlot_OtherFileName_IsNotAWorld(string fileName)
    {
        Assert.False(EnshroudedSlotMap.TryGetSlot(fileName, out _));
    }

    [Theory]
    [InlineData("world3.ksd", 3)]
    [InlineData("world3.ksd.backup-4", 3)]
    [InlineData("WORLD3.KSD.BACKUP-0", 3)]
    public void TryGetSlotOfWorldOrCopy_WorldOrRollingCopy_ReturnsSlot(string fileName, int expected)
    {
        Assert.True(EnshroudedSlotMap.TryGetSlotOfWorldOrCopy(fileName, out var slot));
        Assert.Equal(expected, slot);
    }

    [Theory]
    [InlineData("character0.ksd.backup-1")]
    [InlineData("global.ksd.backup-0")]
    [InlineData("world3.ksd.backup-")]
    public void TryGetSlotOfWorldOrCopy_OtherCopy_IsRefused(string fileName)
    {
        Assert.False(EnshroudedSlotMap.TryGetSlotOfWorldOrCopy(fileName, out _));
    }

    [Fact]
    public void GameCopies_ListsOnlyThisWorldsCopies_NewestFirst()
    {
        var world = Write("world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var backup = temp.Combine("backup");
        Write(Path.Combine("backup", "world1.ksd.backup-9"), EnshroudedSaveBuilder.WorldIdOne);
        Write(Path.Combine("backup", "world1.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdOne);
        Write(Path.Combine("backup", "world1.ksd.backup-2"), EnshroudedSaveBuilder.WorldIdOne);
        Write(Path.Combine("backup", "world11.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdTwo);
        Write(Path.Combine("backup", "character1.ksd.backup-0"), EnshroudedSaveBuilder.WorldIdTwo);

        var copies = EnshroudedSlotMap.GameCopies(world);

        Assert.Equal(
            [Path.Combine(backup, "world1.ksd.backup-0"), Path.Combine(backup, "world1.ksd.backup-2"), Path.Combine(backup, "world1.ksd.backup-9")],
            copies);
    }
}
