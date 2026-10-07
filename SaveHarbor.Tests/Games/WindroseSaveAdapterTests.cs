using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Windrose;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class WindroseSaveAdapterTests
{
    // Nothing these tests do writes app data, so it cannot show up as a profile folder.
    private static WindroseSaveAdapter CreateAdapter(TempDirectory temp) =>
        new(new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Windrose] = new() { SaveRoot = temp.Path }
        }), new TestPathProvider(temp));

    [Fact]
    public async Task DiscoverWorldsAsync_ValidWorld_MapsWorldFields()
    {
        using var temp = new TempDirectory();
        SaveFixtures.CreateWindroseWorld(temp.Path, "12345", "folder1", "TEST_WORLD_ID", "Test World");

        var worlds = await CreateAdapter(temp).DiscoverWorldsAsync(TestContext.Current.CancellationToken);

        var world = Assert.Single(worlds);
        Assert.Equal(GameId.Windrose, world.Game);
        Assert.Equal("TEST_WORLD_ID", world.WorldId);
        Assert.Equal("Test World", world.WorldName);
        Assert.Equal("Medium", world.Subtitle);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_EmptyIslandId_FallsBackToFolderName()
    {
        using var temp = new TempDirectory();
        SaveFixtures.CreateWindroseWorld(temp.Path, "12345", "folder1", string.Empty, "Test World");

        var worlds = await CreateAdapter(temp).DiscoverWorldsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("folder1", Assert.Single(worlds).WorldId);
    }

    [Fact]
    public async Task DiscoverWorldsAsync_FolderWithoutDescription_IsIgnored()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "12345", "RocksDB_v2", "0.10.0", "Worlds", "empty"));

        var worlds = await CreateAdapter(temp).DiscoverWorldsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(worlds);
    }

    [Fact]
    public async Task GetPayloadFiles_WorldWithLockFile_ExcludesLock()
    {
        using var temp = new TempDirectory();
        SaveFixtures.CreateWindroseWorld(temp.Path, "12345", "folder1", "TEST_WORLD_ID", "Test World");
        var adapter = CreateAdapter(temp);
        var world = Assert.Single(await adapter.DiscoverWorldsAsync(TestContext.Current.CancellationToken));

        var files = adapter.GetPayloadFiles(world);

        Assert.DoesNotContain(files, file => Path.GetFileName(file) == "LOCK");
        Assert.Contains(files, file => Path.GetFileName(file) == "CURRENT");
    }

    [Fact]
    public async Task DiscoverSaveRootsAsync_BothRocksDbRoots_PrefersV2()
    {
        using var temp = new TempDirectory();
        SaveFixtures.CreateWindroseWorld(temp.Path, "12345", "folder1", "TEST_WORLD_ID", "Test World", "RocksDB");
        SaveFixtures.CreateWindroseWorld(temp.Path, "12345", "folder2", "TEST_WORLD_ID_2", "Test World 2", "RocksDB_v2");

        var roots = await CreateAdapter(temp).DiscoverSaveRootsAsync(TestContext.Current.CancellationToken);

        var root = Assert.Single(roots);
        Assert.Contains("RocksDB_v2", root.WorldsPath);
    }
}
