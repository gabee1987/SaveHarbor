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
        Root = temp.Combine("Steam", "userdata", "12345", "1203620", "remote");
        LocalTab = temp.Combine("Saved Games", "keen games", "Enshrouded", "default");
        Adapter = new EnshroudedSaveAdapter(new FixedEnshroudedLocator(new EnshroudedSaveRoot(Root, IsSteamCloud: true)));
    }

    // The Steam Cloud folder SaveHarbor manages.
    private string Root { get; }

    // The game's Local tab, which SaveHarbor leaves alone.
    private string LocalTab { get; }

    private EnshroudedSaveAdapter Adapter { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task DiscoverWorldsAsync_ListsOnlyWorldFilesOfTheManagedFolder()
    {
        EnshroudedSaveBuilder.Write(Root, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST_FIRST");
        EnshroudedSaveBuilder.Write(Root, "World12.KSD", EnshroudedSaveBuilder.WorldIdTwo, worldName: "TEST_SECOND");
        EnshroudedSaveBuilder.Write(Root, "character0.ksd", "00000000000000000000000000000003");
        EnshroudedSaveBuilder.Write(Root, "global.ksd", "00000000000000000000000000000004");
        EnshroudedSaveBuilder.Write(Root, "worldX.ksd", "00000000000000000000000000000005");
        EnshroudedSaveBuilder.Write(Root, "world5.ksd_backup", "00000000000000000000000000000006");
        EnshroudedSaveBuilder.Write(Path.Combine(Root, "backup"), "world12.ksd.backup-3", EnshroudedSaveBuilder.WorldIdTwo);
        EnshroudedSaveBuilder.Write(Path.Combine(Root, "migrated"), "world1.ksd", "00000000000000000000000000000007");
        File.WriteAllText(Path.Combine(Root, "world1.ksd"), "TEST_CONTENT");
        EnshroudedSaveBuilder.Write(LocalTab, "world0.ksd", "00000000000000000000000000000008", worldName: "TEST_LOCAL_TAB");

        var worlds = await Adapter.DiscoverWorldsAsync(Token);

        Assert.Equal(["TEST_FIRST", "TEST_SECOND"], worlds.Select(world => world.WorldName).Order());
        var first = Assert.Single(worlds, world => world.WorldName == "TEST_FIRST");
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, first.WorldId);
        Assert.Equal("Slot 1", first.Subtitle);
        Assert.Equal(GameId.Enshrouded, first.Game);
        Assert.Equal(1, first.FileCount);
        Assert.Equal("Slot 13", Assert.Single(worlds, world => world.WorldName == "TEST_SECOND").Subtitle);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_NoStoredName_UsesTheSlotTheGameShows()
    {
        EnshroudedSaveBuilder.Write(Root, "world3.ksd", EnshroudedSaveBuilder.WorldIdOne, worldName: null);

        var world = Assert.Single(await Adapter.DiscoverWorldsAsync(Token));

        Assert.Equal("Enshrouded world 4", world.WorldName);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_NoFolderYet_ListsNothing()
    {
        var adapter = new EnshroudedSaveAdapter(new FixedEnshroudedLocator(null));

        Assert.Empty(await adapter.DiscoverWorldsAsync(Token));
        Assert.Empty(await adapter.DiscoverSaveRootsAsync(Token));
        Assert.Equal(string.Empty, adapter.SaveRootPath);
    }

    [Fact]
    public async Task DiscoverSaveRootsAsync_IsTheManagedFolder()
    {
        EnshroudedSaveBuilder.Write(Root, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);

        var root = Assert.Single(await Adapter.DiscoverSaveRootsAsync(Token));

        Assert.Equal((Root, "Steam Cloud · 1 world(s)"), (root.WorldsPath, root.Detail));
    }

    [Fact]
    public async Task ReadWorldFactsAsync_ShowsLocationSlotCounterAndGameCopies()
    {
        var path = EnshroudedSaveBuilder.Write(Root, "world4.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 7);
        EnshroudedSaveBuilder.Write(Path.Combine(Root, "backup"), "world4.ksd.backup-0", EnshroudedSaveBuilder.WorldIdOne);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        var facts = (await Adapter.ReadWorldFactsAsync(world, Token)).ToDictionary(fact => fact.Label, fact => fact.Value);

        Assert.Equal("Steam Cloud", facts["Location"]);
        Assert.Equal("5", facts["Slot"]);
        Assert.Equal("7", facts["Save counter"]);
        Assert.Equal("1", facts["Game copies kept"]);
    }

    [Fact]
    public async Task GetExpectedWorldPath_ResolvesTheSlot()
    {
        EnshroudedSaveBuilder.Write(Root, "world0.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var root = (await Adapter.DiscoverSaveRootsAsync(Token))[0];

        Assert.Equal(Path.Combine(Root, "world1.ksd"), Adapter.GetExpectedWorldPath(root, EnshroudedSaveBuilder.WorldIdOne));
        Assert.Equal(Path.Combine(Root, "world0.ksd"), Adapter.GetExpectedWorldPath(root, EnshroudedSaveBuilder.WorldIdTwo));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("world0")]
    [InlineData("0000000000000000000000000000000A")]
    [InlineData("")]
    public async Task GetExpectedWorldPath_HostileId_Throws(string worldId)
    {
        Directory.CreateDirectory(Root);
        var root = (await Adapter.DiscoverSaveRootsAsync(Token))[0];

        Assert.Throws<InvalidDataException>(() => Adapter.GetExpectedWorldPath(root, worldId));
    }

    [Fact]
    public void GetSaveFormatVersion_World_IsTheHeaderVersion()
    {
        var path = EnshroudedSaveBuilder.Write(Root, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne);

        Assert.Equal("2", Adapter.GetSaveFormatVersion(path));
        Assert.Null(Adapter.GetSaveFormatVersion(Path.Combine(Root, "world9.ksd")));
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
        var path = EnshroudedSaveBuilder.Write(Root, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var backup = Path.Combine(Root, "backup");
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-9", EnshroudedSaveBuilder.WorldIdOne);
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-1", EnshroudedSaveBuilder.WorldIdOne);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        Assert.Equal(Path.Combine(backup, "world1.ksd.backup-1"), Adapter.GetGameBackupCopyPath(world));
    }

    [Fact]
    public async Task MoveGameFilesAside_MovesOnlyItsRollingCopies()
    {
        var path = EnshroudedSaveBuilder.Write(Root, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var backup = Path.Combine(Root, "backup");
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-8", EnshroudedSaveBuilder.WorldIdOne);
        EnshroudedSaveBuilder.Write(backup, "world1.ksd.backup-9", EnshroudedSaveBuilder.WorldIdOne);
        var other = EnshroudedSaveBuilder.Write(backup, "world2.ksd.backup-9", EnshroudedSaveBuilder.WorldIdTwo);
        var character = EnshroudedSaveBuilder.Write(backup, "character0.ksd.backup-9", EnshroudedSaveBuilder.WorldIdTwo);
        var world = (await Adapter.ReadWorldAsync(path, Token))!;

        var aside = Adapter.MoveGameFilesAside(world);

        Assert.NotNull(aside);
        Assert.StartsWith(Path.Combine(Root, "saveharbor-aside"), aside, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["world1.ksd.backup-8", "world1.ksd.backup-9"], Directory.GetFiles(aside).Select(Path.GetFileName).Order());
        Assert.Empty(EnshroudedSlotMap.GameCopies(path));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(character));
    }

    [Fact]
    public async Task ReadingWorlds_LeavesCharacterGlobalAndSettingsFilesUnchanged()
    {
        var character = EnshroudedSaveBuilder.Write(Root, "character0.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var global = EnshroudedSaveBuilder.Write(Root, "global.ksd", EnshroudedSaveBuilder.WorldIdTwo);
        var settings = Path.Combine(Root, "user_settings.json");
        File.WriteAllText(settings, "{\"TEST\":1}");
        var path = EnshroudedSaveBuilder.Write(Root, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
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
    public void Locator_SaveRootOverride_ReplacesSteamCloud()
    {
        var options = new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Enshrouded] = new() { SaveRoot = temp.Path }
        });

        var root = new EnshroudedSaveRootLocator(options).Locate();

        Assert.Equal(new EnshroudedSaveRoot(temp.Path, IsSteamCloud: false), root);
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

    [Fact]
    public void Locator_ChooseSteamRoot_PrefersTheSignedInAccount()
    {
        var older = new EnshroudedSteamRoot("12345", Directory.CreateDirectory(temp.Combine("a")).FullName);
        var newer = new EnshroudedSteamRoot("67890", Directory.CreateDirectory(temp.Combine("b")).FullName);
        Directory.SetLastWriteTimeUtc(older.WorldsPath, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(older, EnshroudedSaveRootLocator.ChooseSteamRoot([older, newer], "12345"));
        Assert.Equal(newer, EnshroudedSaveRootLocator.ChooseSteamRoot([older, newer], null));
        Assert.Equal(newer, EnshroudedSaveRootLocator.ChooseSteamRoot([older, newer], "99999"));
        Assert.Null(EnshroudedSaveRootLocator.ChooseSteamRoot([], "12345"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Z:\\TEST_MISSING_STEAM")]
    public void Locator_FindSteamRoots_NoSteam_ReturnsEmpty(string? steamPath)
    {
        Assert.Empty(EnshroudedSaveRootLocator.FindSteamRoots(steamPath));
    }

    [Fact]
    public void ValidatePayload_OneWorldFileOfTheExpectedWorld_IsAccepted()
    {
        var payload = temp.Combine("payload");
        EnshroudedSaveBuilder.Write(payload, "world.ksd", EnshroudedSaveBuilder.WorldIdOne);
        Directory.CreateDirectory(Path.Combine(payload, BackupPayloadLayout.GameFilesFolderName));

        Adapter.ValidatePayload(payload, EnshroudedSaveBuilder.WorldIdOne);
    }

    [Theory]
    [InlineData("other-world")]
    [InlineData("two-files")]
    [InlineData("wrong-extension")]
    [InlineData("not-a-save")]
    public void ValidatePayload_AnythingElse_IsRefused(string shape)
    {
        var payload = temp.Combine("payload");
        Directory.CreateDirectory(payload);
        switch (shape)
        {
            case "other-world":
                EnshroudedSaveBuilder.Write(payload, "world.ksd", EnshroudedSaveBuilder.WorldIdTwo);
                break;
            case "two-files":
                EnshroudedSaveBuilder.Write(payload, "world.ksd", EnshroudedSaveBuilder.WorldIdOne);
                EnshroudedSaveBuilder.Write(payload, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
                break;
            case "wrong-extension":
                EnshroudedSaveBuilder.Write(payload, "world.sav", EnshroudedSaveBuilder.WorldIdOne);
                break;
            default:
                File.WriteAllText(Path.Combine(payload, "world.ksd"), "TEST_CONTENT");
                break;
        }

        Assert.Throws<InvalidDataException>(() => Adapter.ValidatePayload(payload, EnshroudedSaveBuilder.WorldIdOne));
    }

    [Fact]
    public void GetArchiveFileName_DropsTheSlot()
    {
        Assert.Equal("world.ksd", Adapter.GetArchiveFileName(Path.Combine(Root, "world7.ksd")));
    }
}
