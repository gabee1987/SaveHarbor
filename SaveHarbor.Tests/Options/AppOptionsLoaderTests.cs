using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Options;

public sealed class AppOptionsLoaderTests
{
    [Fact]
    public void ReadGameArgument_KnownGame_ReturnsGame()
    {
        Assert.Equal(GameId.Dragonwilds, AppOptionsLoader.ReadGameArgument(["app.exe", "--game", "dragonwilds"]));
    }

    [Fact]
    public void ReadGameArgument_UnknownGame_ReturnsNull()
    {
        Assert.Null(AppOptionsLoader.ReadGameArgument(["--game", "unknown"]));
    }

    [Fact]
    public void ReadGameArgument_MissingArgument_ReturnsNull()
    {
        Assert.Null(AppOptionsLoader.ReadGameArgument(["app.exe"]));
    }
}
