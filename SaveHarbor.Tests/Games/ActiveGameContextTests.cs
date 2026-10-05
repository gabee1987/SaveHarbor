using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class ActiveGameContextTests
{
    private static StubGameRegistry CreateRegistry() =>
        new(new StubGameDefinition(GameId.Windrose), new StubGameDefinition(GameId.Dragonwilds));

    [Fact]
    public void Constructor_NoSettings_DefaultsToWindrose()
    {
        using var temp = new TempDirectory();

        var context = new ActiveGameContext(CreateRegistry(), new TestPathProvider(temp), null);

        Assert.Equal(GameId.Windrose, context.Current.Id);
    }

    [Fact]
    public void SetActive_NewGame_PersistsForNextInstance()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        new ActiveGameContext(CreateRegistry(), paths, null).SetActive(GameId.Dragonwilds);

        var restored = new ActiveGameContext(CreateRegistry(), paths, null);

        Assert.Equal(GameId.Dragonwilds, restored.Current.Id);
    }

    [Fact]
    public void Constructor_StartupOverride_WinsOverSavedGame()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        new ActiveGameContext(CreateRegistry(), paths, null).SetActive(GameId.Dragonwilds);

        var context = new ActiveGameContext(CreateRegistry(), paths, GameId.Windrose);

        Assert.Equal(GameId.Windrose, context.Current.Id);
    }

    [Fact]
    public void Constructor_CorruptSettings_FallsBackToWindrose()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        Directory.CreateDirectory(paths.AppDataRoot);
        File.WriteAllText(paths.AppSettingsPath, "{ not json");

        var context = new ActiveGameContext(CreateRegistry(), paths, null);

        Assert.Equal(GameId.Windrose, context.Current.Id);
    }
}
