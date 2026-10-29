using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// The world settings block is read from an observed, undocumented layout, so anything that does not frame exactly or
// holds an implausible value must give no settings at all rather than wrong ones.
public sealed class EnshroudedWorldSettingsTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static IReadOnlyList<EnshroudedWorldSetting>? Parse(byte[]? fields, string worldName = "TEST_WORLD") =>
        EnshroudedWorldSettingsReader.TryParse(EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, worldName: worldName, settingsFields: fields));

    private static byte[] ObservedWith(int offset, params byte[] bytes)
    {
        var fields = EnshroudedSaveBuilder.ObservedSettingsFields.ToArray();
        bytes.CopyTo(fields, offset);
        return fields;
    }

    private static string ValueOf(IReadOnlyList<EnshroudedWorldSetting> settings, string label) => settings.Single(setting => setting.Label == label).Value;

    [Fact]
    public void TryParse_ObservedBlock_ReadsEverySetting()
    {
        var settings = Parse(EnshroudedSaveBuilder.ObservedSettingsFields);

        Assert.NotNull(settings);
        Assert.Equal(37, settings.Count);
        Assert.Equal("×1", ValueOf(settings, "Player health"));
        Assert.Equal("On", ValueOf(settings, "Durability"));
        Assert.Equal("Off", ValueOf(settings, "Starving debuff"));
        Assert.Equal("10 min", ValueOf(settings, "From hunger to starving"));
        Assert.Equal("Normal", ValueOf(settings, "Weather frequency"));
        Assert.Equal("×2", ValueOf(settings, "Mining damage"));
        Assert.Equal("×1.25", ValueOf(settings, "Plant growth speed"));
        Assert.Equal("×1.5", ValueOf(settings, "Resource drop amount"));
        Assert.Equal("×0.75", ValueOf(settings, "Perk cost"));
        Assert.Equal("Off", ValueOf(settings, "Pacify all enemies"));
        Assert.Equal("30 min", ValueOf(settings, "Day length"));
        Assert.Equal("12 min", ValueOf(settings, "Night length"));
    }

    [Fact]
    public void TryParse_LongerWorldName_FindsTheBlockAfterIt()
    {
        var settings = Parse(EnshroudedSaveBuilder.ObservedSettingsFields, worldName: "TEST_WORLD_WITH_A_MUCH_LONGER_NAME");

        Assert.Equal("×2", ValueOf(settings!, "Mining damage"));
    }

    [Fact]
    public void TryParse_UnknownChoice_IsShownByNumber()
    {
        // Byte 78 is "Random spawner amount"; a later game version may add choices.
        var settings = Parse(ObservedWith(78, 4));

        Assert.Equal("Setting 4", ValueOf(settings!, "Random spawner amount"));
    }

    [Fact]
    public void TryParse_NoSettingsBlock_ReturnsNull() => Assert.Null(Parse(null));

    [Fact]
    public void TryParse_SwitchOutOfRange_ReturnsNull() => Assert.Null(Parse(ObservedWith(20, 2)));

    [Fact]
    public void TryParse_FactorNotFinite_ReturnsNull() => Assert.Null(Parse(ObservedWith(0, 0x00, 0x00, 0xC0, 0x7F)));

    [Fact]
    public void TryParse_DurationImplausible_ReturnsNull() => Assert.Null(Parse(ObservedWith(26, 0, 0, 0, 0, 0, 0, 0, 0)));

    [Fact]
    public void TryParse_OtherBlockType_ReturnsNull()
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, settingsFields: EnshroudedSaveBuilder.ObservedSettingsFields);
        // Header, name block ("TEST_WORLD" is 10 bytes) and its 18-byte tail.
        var block = EnshroudedSaveHeader.Size + 75 + 10 + 18;
        Assert.Equal((byte)'S', bytes[block]);
        bytes[block + 8] = 2;

        Assert.Null(EnshroudedWorldSettingsReader.TryParse(bytes));
    }

    [Fact]
    public void TryParse_BlockLongerThanExpected_ReturnsNull()
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, settingsFields: EnshroudedSaveBuilder.ObservedSettingsFields);
        var nextBlock = bytes.Length - 64 - 4;
        bytes[nextBlock] = 0;

        Assert.Null(EnshroudedWorldSettingsReader.TryParse(bytes));
    }

    [Fact]
    public async Task ReadWorldFacts_WithSettings_ShowsTheSummaryAsRules()
    {
        var world = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne, settingsFields: EnshroudedSaveBuilder.ObservedSettingsFields));

        var rules = (await harness.EnshroudedAdapter.ReadWorldFactsAsync(world, TestContext.Current.CancellationToken)).Where(fact => fact.Group == WorldFactGroup.Rules).ToArray();

        Assert.Equal(EnshroudedWorldSettingsReader.SummaryLabels, rules.Select(rule => rule.Label));
        Assert.Contains(rules, rule => rule.Label == "Mining damage" && rule.Value == "×2");
    }

    [Fact]
    public async Task InspectWorld_WithSettings_ListsThemInTheirOwnSection()
    {
        var world = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne, settingsFields: EnshroudedSaveBuilder.ObservedSettingsFields));

        var sections = await harness.EnshroudedAdapter.InspectWorldAsync(world, TestContext.Current.CancellationToken);

        Assert.Equal(37, sections.Single(section => section.Title == "World settings").Items.Count);
        Assert.DoesNotContain(sections.Single(section => section.Title == "World").Items, item => item.Label == "Mining damage");
    }

    [Fact]
    public async Task InspectWorld_WithoutSettings_SaysSoAndShowsNoRules()
    {
        var world = await harness.ReadEnshroudedWorldAsync(EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, "world0.ksd", EnshroudedSaveBuilder.WorldIdOne));

        var sections = await harness.EnshroudedAdapter.InspectWorldAsync(world, TestContext.Current.CancellationToken);
        var facts = await harness.EnshroudedAdapter.ReadWorldFactsAsync(world, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(sections, section => section.Title == "World settings");
        Assert.Contains(sections.Single(section => section.Title == "World").Items, item => item.Label == "World settings" && item.Value == "Not stored in a readable place");
        Assert.DoesNotContain(facts, fact => fact.Group == WorldFactGroup.Rules);
    }
}
