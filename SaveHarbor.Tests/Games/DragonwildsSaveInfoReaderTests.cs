using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class DragonwildsSaveInfoReaderTests
{
    private const string TestPassword = "TEST_SECRET_VALUE";

    private static DragonwildsSaveBuilder CompleteSave(string password = "") => new DragonwildsSaveBuilder()
        .Int32("VERSION", 9)
        .Int32("GUID_A", 12345)
        .String("WorldName", "TEST_WORLD")
        .Byte("FriendlyFire", 0)
        .Int32("SurvivalDifficulty", 3)
        .Single("Difficulty.Progression.XPGainScale", 1.2f)
        .Single("Difficulty.Progression.CraftingCostScale", 0.5f)
        .Ticks("TimeOfSave", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))
        .String("SessionPasswd", password)
        .Int32("CrossplayEnabled", 1)
        .String("WorldOwnerId", "TEST_OWNER_ID")
        .String("WorldNameOwner", "TEST_USER")
        .String("LastSavedBy", "++dominion+hotfix:12345, ++dominion+live:12000");

    [Fact]
    public void Parse_CompleteSave_ReadsAllowListedValues()
    {
        var info = DragonwildsSaveInfoReader.Parse(CompleteSave().Build());

        Assert.NotNull(info);
        Assert.Equal("TEST_WORLD", info.WorldName);
        Assert.Equal("TEST_USER", info.CreatedBy);
        Assert.Equal(3, info.Difficulty);
        Assert.False(info.FriendlyFire);
        Assert.True(info.Crossplay);
        Assert.False(info.PasswordProtected);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), info.SavedAtUtc);
        Assert.Equal("12345", info.GameBuild);
        Assert.Equal(1.2f, info.RuleScales["Difficulty.Progression.XPGainScale"]);
    }

    [Fact]
    public void Facts_PasswordSet_ReportsProtectionButNeverTheSecrets()
    {
        var info = DragonwildsSaveInfoReader.Parse(CompleteSave(TestPassword).Build());

        var facts = DragonwildsWorldFacts.From(info!);

        Assert.Contains(facts, fact => fact.Label == "Access" && fact.Value == "Password");
        Assert.DoesNotContain(facts, fact => fact.Value.Contains(TestPassword) || fact.Value.Contains("TEST_OWNER_ID"));
    }

    [Fact]
    public void Facts_CustomDifficultyAndRules_AreFormatted()
    {
        var facts = DragonwildsWorldFacts.From(DragonwildsSaveInfoReader.Parse(CompleteSave().Build())!);

        Assert.Contains(new WorldFact(WorldFactGroup.Details, "Difficulty", "Custom"), facts);
        Assert.Contains(new WorldFact(WorldFactGroup.Details, "PvP", "Off"), facts);
        Assert.Contains(new WorldFact(WorldFactGroup.Rules, "XP gain", "×1.2"), facts);
        Assert.Contains(new WorldFact(WorldFactGroup.Rules, "Crafting cost", "×0.5"), facts);
    }

    [Fact]
    public void Parse_WrongMagic_ReturnsNull()
    {
        var bytes = CompleteSave().Build();
        bytes[0] = (byte)'G';

        Assert.Null(DragonwildsSaveInfoReader.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedFile_ReturnsNull()
    {
        var bytes = CompleteSave().Build();

        Assert.Null(DragonwildsSaveInfoReader.Parse(bytes.AsSpan(0, bytes.Length / 3)));
    }

    [Fact]
    public void Parse_OversizedString_ReturnsNull()
    {
        var bytes = new DragonwildsSaveBuilder().Raw("WorldName", [0xFF, 0xFF, 0x00, 0x00, 0x41]).Build();

        Assert.Null(DragonwildsSaveInfoReader.Parse(bytes));
    }

    [Fact]
    public async Task ReadWorldFactsAsync_NonSaveFile_ReturnsNoFacts()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("TEST_WORLD.sav");
        File.WriteAllText(path, "TEST_CONTENT");
        var adapter = new DragonwildsSaveAdapter(new GameOptionsProvider(new Dictionary<GameId, GameOptions>()));
        var world = await adapter.ReadWorldAsync(path, TestContext.Current.CancellationToken);

        var facts = await adapter.ReadWorldFactsAsync(world!, TestContext.Current.CancellationToken);

        Assert.Empty(facts);
    }
}
