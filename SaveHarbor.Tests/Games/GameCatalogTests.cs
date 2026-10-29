using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Games;

// Every game in GameId needs the few per-game pieces that live outside its own folder. A game added without one of
// them would otherwise fail only at run time (a missing shell throws, a missing text shows its raw key).
public sealed class GameCatalogTests
{
    public static TheoryData<GameId> Games() => new(Enum.GetValues<GameId>());

    private static string AppFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveHarbor.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output."), "SaveHarbor.App");
    }

    [Fact]
    public void GameId_ListsTheThreeGames()
    {
        Assert.Equal([GameId.Windrose, GameId.Dragonwilds, GameId.Enshrouded], Enum.GetValues<GameId>());
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void StorageKey_RoundTrips(GameId game)
    {
        Assert.True(GameIdExtensions.TryParseStorageKey(game.ToStorageKey(), out var parsed));
        Assert.Equal(game, parsed);
    }

    [Fact]
    public void StorageKey_Enshrouded_MatchesTheCloudGamesRow()
    {
        Assert.Equal("enshrouded", GameId.Enshrouded.ToStorageKey());
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void LoadGameOptions_EveryGame_HasALaunchUri(GameId game)
    {
        var options = AppOptionsLoader.LoadGameOptions();

        Assert.StartsWith("steam://rungameid/", options[game].LaunchUri, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadGameOptions_Enshrouded_LaunchesItsSteamApp()
    {
        Assert.Equal("steam://rungameid/1203620", AppOptionsLoader.LoadGameOptions()[GameId.Enshrouded].LaunchUri);
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void MainWindow_EveryGame_HasAShellTemplate(GameId game)
    {
        var xaml = File.ReadAllText(Path.Combine(AppFolder(), "MainWindow.xaml"));

        Assert.Contains($"x:Key=\"{game}ShellTemplate\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void UiText_EveryGame_HasAPrivacyNote(GameId game)
    {
        var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppFolder(), "Resources", "Texts", "ui-text.en.json")))!;

        Assert.True(texts.ContainsKey($"Inspector.PrivacyNote.{game}"));
    }
}
