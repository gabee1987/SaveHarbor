using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class EnshroudedSaveAdapterTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public EnshroudedSaveAdapterTests()
    {
        LocalRoot = temp.Combine("Saved Games", "keen games", "Enshrouded", "default");
        SteamRoot = temp.Combine("Steam", "userdata", "12345", "1203620", "remote");
        Adapter = new EnshroudedSaveAdapter(new FixedLocator(new EnshroudedSaveRoots(LocalRoot, [new EnshroudedSteamRoot("12345", SteamRoot)])));
    }

    private string LocalRoot { get; }

    private string SteamRoot { get; }

    private EnshroudedSaveAdapter Adapter { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => temp.Dispose();

    private sealed class FixedLocator(EnshroudedSaveRoots roots) : IEnshroudedSaveRootLocator
    {
        public EnshroudedSaveRoots Locate() => roots;
    }

    [Fact]
    public async Task DiscoverWorldsAsync_LocalAndSteamFolders_ListsOnlyWorldFiles()
    {
        EnshroudedSaveBuilder.Write(LocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST_LOCAL");
        EnshroudedSaveBuilder.Write(SteamRoot, "World12.KSD", EnshroudedSaveBuilder.WorldIdTwo, worldName: "TEST_STEAM");
        EnshroudedSaveBuilder.Write(SteamRoot, "character0.ksd", "00000000000000000000000000000003");
        EnshroudedSaveBuilder.Write(SteamRoot, "global.ksd", "00000000000000000000000000000004");
        EnshroudedSaveBuilder.Write(SteamRoot, "worldX.ksd", "00000000000000000000000000000005");
        EnshroudedSaveBuilder.Write(SteamRoot, "world5.ksd_backup", "00000000000000000000000000000006");
        EnshroudedSaveBuilder.Write(Path.Combine(SteamRoot, "backup"), "world12.ksd.backup-3", EnshroudedSaveBuilder.WorldIdTwo);
        EnshroudedSaveBuilder.Write(Path.Combine(SteamRoot, "migrated"), "world1.ksd", "00000000000000000000000000000007");
        File.WriteAllText(Path.Combine(LocalRoot, "world1.ksd"), "TEST_CONTENT");

        var worlds = await Adapter.DiscoverWorldsAsync(Token);

        Assert.Equal(2, worlds.Count);
        var local = Assert.Single(worlds, world => world.WorldName == "TEST_LOCAL");
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, local.WorldId);
        Assert.Equal("Slot 0 · Local", local.Subtitle);
        Assert.False(local.IsReadOnly);
        Assert.Equal(GameId.Enshrouded, local.Game);
        Assert.Equal(1, local.FileCount);
        var steam = Assert.Single(worlds, world => world.WorldName == "TEST_STEAM");
        Assert.Equal("Slot 12 · Steam Cloud", steam.Subtitle);
        Assert.True(steam.IsReadOnly);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_NoStoredName_UsesTheSlot()
    {
        EnshroudedSaveBuilder.Write(LocalRoot, "world3.ksd", EnshroudedSaveBuilder.WorldIdOne, worldName: null);

        var world = Assert.Single(await Adapter.DiscoverWorldsAsync(Token));

        Assert.Equal("Enshrouded world 3", world.WorldName);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_SameWorldInBothFolders_ListsLocalFirst()
    {
        var steam = EnshroudedSaveBuilder.Write(SteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var local = EnshroudedSaveBuilder.Write(LocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var sameTime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(steam, sameTime);
        File.SetLastWriteTimeUtc(local, sameTime);

        var worlds = await Adapter.DiscoverWorldsAsync(Token);

        Assert.Equal([local, steam], worlds.Select(world => world.SavePath));
    }

    [Fact]
    public async Task DiscoverSaveRootsAsync_LocalNotCreatedYet_IsStillListedAndOnlyItReceivesWorlds()
    {
        EnshroudedSaveBuilder.Write(SteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);

        var roots = await Adapter.DiscoverSaveRootsAsync(Token);

        Assert.Equal(2, roots.Count);
        Assert.Equal(("local", LocalRoot, "Local · not created yet", true), (roots[0].RootId, roots[0].WorldsPath, roots[0].Detail, roots[0].CanReceiveWorlds));
        Assert.Equal(("steam-12345", SteamRoot, "Steam Cloud · 1 world(s)", false), (roots[1].RootId, roots[1].WorldsPath, roots[1].Detail, roots[1].CanReceiveWorlds));
    }

    [Fact]
    public async Task ReadWorldAsync_FileInSteamFolder_IsReadOnly()
    {
        var path = EnshroudedSaveBuilder.Write(SteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);

        var world = await Adapter.ReadWorldAsync(path, Token);

        Assert.True(world!.IsReadOnly);
        Assert.Contains(new WorldFact(WorldFactGroup.Details, "Location", "Steam Cloud"), await Adapter.ReadWorldFactsAsync(world, Token));
    }

    [Fact]
    public async Task ReadWorldFactsAsync_ShowsSlotCounterAndGameCopies()
    {
        var path = EnshroudedSaveBuilder.Write(LocalRoot, "world4.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 7);
        EnshroudedSaveBuilder.Write(Path.Combine(LocalRoot, "backup"), "world4.ksd.backup-0", EnshroudedSaveBuilder.WorldIdOne);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        var facts = (await Adapter.ReadWorldFactsAsync(world, Token)).ToDictionary(fact => fact.Label, fact => fact.Value);

        Assert.Equal("Local", facts["Location"]);
        Assert.Equal("4", facts["Slot"]);
        Assert.Equal("7", facts["Save counter"]);
        Assert.Equal("1", facts["Game copies kept"]);
    }

    [Fact]
    public async Task GetExpectedWorldPath_LocalRoot_ResolvesTheSlot()
    {
        EnshroudedSaveBuilder.Write(LocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var local = (await Adapter.DiscoverSaveRootsAsync(Token))[0];

        Assert.Equal(Path.Combine(LocalRoot, "world1.ksd"), Adapter.GetExpectedWorldPath(local, EnshroudedSaveBuilder.WorldIdOne));
        Assert.Equal(Path.Combine(LocalRoot, "world0.ksd"), Adapter.GetExpectedWorldPath(local, EnshroudedSaveBuilder.WorldIdTwo));
    }

    [Fact]
    public async Task GetExpectedWorldPath_SteamRoot_IsRefused()
    {
        Directory.CreateDirectory(SteamRoot);
        var steam = (await Adapter.DiscoverSaveRootsAsync(Token))[1];

        Assert.Throws<InvalidOperationException>(() => Adapter.GetExpectedWorldPath(steam, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("world0")]
    [InlineData("0000000000000000000000000000000A")]
    [InlineData("")]
    public async Task GetExpectedWorldPath_HostileId_Throws(string worldId)
    {
        var local = (await Adapter.DiscoverSaveRootsAsync(Token))[0];

        Assert.Throws<InvalidDataException>(() => Adapter.GetExpectedWorldPath(local, worldId));
    }

    [Fact]
    public void ValidatePayload_UntilPlacingIsImplemented_RefusesEveryPayload()
    {
        Assert.Throws<InvalidDataException>(() => Adapter.ValidatePayload(temp.Path, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void GetSaveFormatVersion_World_IsTheHeaderVersion()
    {
        var path = EnshroudedSaveBuilder.Write(LocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne);

        Assert.Equal("2", Adapter.GetSaveFormatVersion(path));
        Assert.Null(Adapter.GetSaveFormatVersion(Path.Combine(LocalRoot, "world9.ksd")));
    }

    [Theory]
    [InlineData("world2.ksd")]
    [InlineData("world2.ksd.backup-4")]
    public async Task ReadImportCandidateAsync_WorldOrRollingCopy_IsAccepted(string fileName)
    {
        var path = EnshroudedSaveBuilder.Write(temp.Combine("picked"), fileName, EnshroudedSaveBuilder.WorldIdOne, saveCounter: 9);

        var candidate = await Adapter.ReadImportCandidateAsync(path, Token);

        Assert.NotNull(candidate);
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, candidate.World.WorldId);
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, candidate.Fingerprint);
        Assert.Equal(9, candidate.Revision);
    }

    [Theory]
    [InlineData("character0.ksd")]
    [InlineData("global.ksd")]
    [InlineData("world2.sav")]
    public async Task ReadImportCandidateAsync_NotAWorldFile_IsRefused(string fileName)
    {
        var path = EnshroudedSaveBuilder.Write(temp.Combine("picked"), fileName, EnshroudedSaveBuilder.WorldIdOne);

        Assert.Null(await Adapter.ReadImportCandidateAsync(path, Token));
    }

    [Fact]
    public async Task ReadImportCandidateAsync_WorldNameButNoValidHeader_IsRefused()
    {
        var path = temp.Combine("picked", "world2.ksd");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "TEST_CONTENT_THAT_IS_LONGER_THAN_FORTY_EIGHT_BYTES_AND_NOT_A_SAVE");

        Assert.Null(await Adapter.ReadImportCandidateAsync(path, Token));
    }

    [Fact]
    public async Task GetGameBackupCopyPath_ReturnsTheNewestRollingCopy()
    {
        var path = EnshroudedSaveBuilder.Write(LocalRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var backup = Path.Combine(LocalRoot, "backup");
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-9", EnshroudedSaveBuilder.WorldIdOne);
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-1", EnshroudedSaveBuilder.WorldIdOne);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        Assert.Equal(Path.Combine(backup, "world1.ksd.backup-1"), Adapter.GetGameBackupCopyPath(world));
    }

    [Fact]
    public async Task MoveGameFilesAside_LocalWorld_MovesOnlyItsRollingCopies()
    {
        var path = EnshroudedSaveBuilder.Write(LocalRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var backup = Path.Combine(LocalRoot, "backup");
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-8", EnshroudedSaveBuilder.WorldIdOne);
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-9", EnshroudedSaveBuilder.WorldIdOne);
        var other = EnshroudedSaveBuilder.Write(backup, "world2.ksd.backup-9", EnshroudedSaveBuilder.WorldIdTwo);
        var character = EnshroudedSaveBuilder.Write(backup, "character0.ksd.backup-9", EnshroudedSaveBuilder.WorldIdTwo);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        var aside = Adapter.MoveGameFilesAside(world);

        Assert.NotNull(aside);
        Assert.StartsWith(Path.Combine(LocalRoot, "saveharbor-aside"), aside, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["world1.ksd.backup-8", "world1.ksd.backup-9"], Directory.GetFiles(aside).Select(Path.GetFileName).Order());
        Assert.Empty(EnshroudedSlotMap.GameCopies(path));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(character));
    }

    [Fact]
    public async Task MoveGameFilesAside_SteamWorld_MovesNothing()
    {
        var path = EnshroudedSaveBuilder.Write(SteamRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var copy = EnshroudedSaveBuilder.Write(Path.Combine(SteamRoot, "backup"), "world1.ksd.backup-9", EnshroudedSaveBuilder.WorldIdOne);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        Assert.Null(Adapter.MoveGameFilesAside(world));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task ReadingWorlds_LeavesCharacterGlobalAndSettingsFilesUnchanged()
    {
        var character = EnshroudedSaveBuilder.Write(LocalRoot, "character0.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var global = EnshroudedSaveBuilder.Write(LocalRoot, "global.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var settings = Path.Combine(LocalRoot, "user_settings.json");
        File.WriteAllText(settings, "{\"TEST\":1}");
        var path = EnshroudedSaveBuilder.Write(LocalRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        string[] untouched = [character, global, settings];
        var before = untouched.Select(File.ReadAllBytes).ToArray();
        var writeTimes = untouched.Select(File.GetLastWriteTimeUtc).ToArray();

        foreach (var world in await Adapter.DiscoverWorldsAsync(Token))
        {
            await Adapter.ReadWorldFactsAsync(world, Token);
            await Adapter.InspectWorldAsync(world, Token);
        }

        await Adapter.DiscoverSaveRootsAsync(Token);
        await Adapter.ReadImportCandidateAsync(path, Token);
        Assert.Equal(before, untouched.Select(File.ReadAllBytes));
        Assert.Equal(writeTimes, untouched.Select(File.GetLastWriteTimeUtc));
    }

    [Fact]
    public void Locator_SaveRootOverride_IsTheOnlyRoot()
    {
        var options = new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Enshrouded] = new() { SaveRoot = temp.Path }
        });

        var roots = new EnshroudedSaveRootLocator(options).Locate();

        Assert.Equal(temp.Path, roots.LocalRoot);
        Assert.Empty(roots.SteamRoots);
    }

    [Fact]
    public void Locator_FindSteamRoots_ListsEveryAccountThatHasTheGame()
    {
        var steam = temp.Combine("Steam");
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "67890", "1203620", "remote"));
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "12345", "1203620", "remote"));
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "11111", "3041230", "remote"));
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "anonymous", "1203620", "remote"));

        var roots = EnshroudedSaveRootLocator.FindSteamRoots(steam);

        Assert.Equal(["12345", "67890"], roots.Select(root => root.SteamUserId));
        Assert.Equal(Path.Combine(steam, "userdata", "12345", "1203620", "remote"), roots[0].WorldsPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Z:\\TEST_MISSING_STEAM")]
    public void Locator_FindSteamRoots_NoSteam_ReturnsEmpty(string? steamPath)
    {
        Assert.Empty(EnshroudedSaveRootLocator.FindSteamRoots(steamPath));
    }
}
