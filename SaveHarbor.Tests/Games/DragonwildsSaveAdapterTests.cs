using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class DragonwildsSaveAdapterTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private DragonwildsSaveAdapter CreateAdapter(string? root = null) =>
        new(new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Dragonwilds] = new() { SaveRoot = root ?? temp.Path }
        }));

    private void Touch(params string[] relativePath)
    {
        var path = temp.Combine(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "TEST_CONTENT");
    }

    [Fact]
    public async Task DiscoverWorldsAsync_MixedFolder_ReturnsOnlySavFiles()
    {
        Touch("World One.sav");
        Touch("World One.sav.bak");
        Touch("World Two.backup.sav");
        Touch("Settings.json");
        Touch("Archive.save");
        Touch("SaveCharacters", "Hero.sav");

        var worlds = await CreateAdapter().DiscoverWorldsAsync(TestContext.Current.CancellationToken);

        var world = Assert.Single(worlds);
        Assert.Equal("World One", world.WorldName);
        Assert.Equal(GameId.Dragonwilds, world.Game);
        Assert.Equal(1, world.FileCount);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_NameWithMiddleDotAndSpace_KeepsDisplayName()
    {
        Touch("My·World Test.sav");

        var worlds = await CreateAdapter().DiscoverWorldsAsync(TestContext.Current.CancellationToken);

        var world = Assert.Single(worlds);
        Assert.Equal("My·World Test", world.WorldName);
        Assert.Equal("My·World Test", world.WorldId);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_MissingRoot_ReturnsEmpty()
    {
        var adapter = CreateAdapter(temp.Combine("does-not-exist"));

        Assert.Empty(await adapter.DiscoverWorldsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await adapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadWorldAsync_SameFileTwice_GivesSameWorldId()
    {
        Touch("World One.sav");
        var path = temp.Combine("World One.sav");
        var adapter = CreateAdapter();

        var first = await adapter.ReadWorldAsync(path, TestContext.Current.CancellationToken);
        var second = await adapter.ReadWorldAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(first!.WorldId, second!.WorldId);
    }

    [Fact]
    public async Task ReadWorldAsync_NonSavExtension_ReturnsNull()
    {
        Touch("World One.json");

        var world = await CreateAdapter().ReadWorldAsync(temp.Combine("World One.json"), TestContext.Current.CancellationToken);

        Assert.Null(world);
    }

    [Fact]
    public async Task DiscoverSaveRootsAsync_ExistingRoot_ReportsWorldCount()
    {
        Touch("World One.sav");
        Touch("World Two.sav");

        var root = Assert.Single(await CreateAdapter().DiscoverSaveRootsAsync(TestContext.Current.CancellationToken));

        Assert.Equal("2 world(s)", root.Detail);
        Assert.Equal(root.RootPath, root.WorldsPath);
    }

    [Fact]
    public async Task GetExpectedWorldPath_ValidId_AddsSavExtensionUnderRoot()
    {
        var adapter = CreateAdapter();
        var root = Assert.Single(await adapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken));

        var path = adapter.GetExpectedWorldPath(root, "World One");

        Assert.Equal(temp.Combine("World One.sav"), path, ignoreCase: true);
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("C:\\x")]
    [InlineData("")]
    public async Task GetExpectedWorldPath_HostileId_Throws(string worldId)
    {
        var adapter = CreateAdapter();
        var root = Assert.Single(await adapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken));

        Assert.Throws<InvalidDataException>(() => adapter.GetExpectedWorldPath(root, worldId));
    }
}
