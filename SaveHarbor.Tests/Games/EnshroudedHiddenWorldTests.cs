using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// A world file Steam does not track (for example one downloaded into the slot of a world deleted in the game) is hidden
// from the game and not restored by Steam, so SaveHarbor may remove it itself. Anything it cannot tell for certain
// keeps the "delete it in the game" rule.
public sealed class EnshroudedHiddenWorldTests : IDisposable
{
    private const string Cache =
        """
        "1203620"
        {
        	"ChangeNumber"		"66"
        	"ostype"		"-184"
        	"world0.ksd"
        	{
        		"root"		"0"
        		"size"		"128"
        		"sha"		"0000000000000000000000000000000000000000"
        		"syncstate"		"1"
        	}
        	"character0.ksd"
        	{
        		"root"		"0"
        	}
        }
        """;

    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string CachePath => Path.Combine(Path.GetDirectoryName(harness.EnshroudedRoot)!, EnshroudedSteamCloudCache.FileName);

    private string WriteWorld(string fileName, string worldId) => EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, fileName, worldId);

    private EnshroudedGameDefinition Definition() =>
        new(new GameOptionsProvider(new Dictionary<GameId, GameOptions>()), harness.EnshroudedAdapter);

    [Fact]
    public void TryListFiles_SteamCache_ListsTheFilesOnly()
    {
        var files = EnshroudedSteamCloudCache.TryListFiles(Cache);

        Assert.NotNull(files);
        Assert.Equal(["character0.ksd", "world0.ksd"], files.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"1203620\" { \"world0.ksd\" { }")]
    [InlineData("\"1203620\" { \"world0.ksd\" { } } }")]
    [InlineData("\"1203620\" { \"world0.ksd { } }")]
    [InlineData("\"1203620\" { world0.ksd { } }")]
    [InlineData("\"1203620\" { \"world0.ksd\" } ")]
    [InlineData("\"1203620\" { } \"1203621\" { }")]
    public void TryListFiles_NotASteamCache_ReturnsNull(string text) => Assert.Null(EnshroudedSteamCloudCache.TryListFiles(text));

    [Fact]
    public void IsTracked_ReadsSteamsList()
    {
        var tracked = WriteWorld("world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var hidden = WriteWorld("world2.ksd", EnshroudedSaveBuilder.WorldIdTwo);

        Assert.Null(EnshroudedSteamCloudCache.IsTracked(hidden));

        File.WriteAllText(CachePath, Cache);
        Assert.True(EnshroudedSteamCloudCache.IsTracked(tracked));
        Assert.False(EnshroudedSteamCloudCache.IsTracked(hidden));

        File.WriteAllText(CachePath, "TEST_CONTENT");
        Assert.Null(EnshroudedSteamCloudCache.IsTracked(hidden));
    }

    [Fact]
    public async Task RemoveHint_OnlyAWorldSteamDoesNotTrack_IsRemovedBySaveHarbor()
    {
        var tracked = await harness.ReadEnshroudedWorldAsync(WriteWorld("world0.ksd", EnshroudedSaveBuilder.WorldIdOne));
        var hidden = await harness.ReadEnshroudedWorldAsync(WriteWorld("world2.ksd", EnshroudedSaveBuilder.WorldIdTwo));

        Assert.NotNull(Definition().GetRemoveWorldInGameHint(hidden));

        File.WriteAllText(CachePath, Cache);
        Assert.NotNull(Definition().GetRemoveWorldInGameHint(tracked));
        Assert.Null(Definition().GetRemoveWorldInGameHint(hidden));
    }

    [Fact]
    public async Task Remove_HiddenWorld_KeepsANewWorldOutOfItsSlot()
    {
        WriteWorld("world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var hiddenPath = WriteWorld("world1.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        EnshroudedSaveBuilder.Write(Path.Combine(harness.EnshroudedRoot, "backup"), "world1.ksd.backup-0", EnshroudedSaveBuilder.WorldIdTwo);
        File.WriteAllText(CachePath, Cache);

        var backup = await harness.Backups.RemoveWorldAsync(await harness.ReadEnshroudedWorldAsync(hiddenPath), Token);

        Assert.False(File.Exists(hiddenPath));
        Assert.True(File.Exists(backup.FilePath));
        Assert.Equal(
            Path.Combine(harness.EnshroudedRoot, "world2.ksd"),
            EnshroudedSlotMap.ResolveTargetPath(harness.EnshroudedRoot, "00000000000000000000000000000099"));
    }
}
