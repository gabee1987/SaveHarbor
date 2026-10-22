using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Migrations;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Migrations;

public sealed class SyncStateProviderScopeMigratorTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly TestPathProvider paths;

    public SyncStateProviderScopeMigratorTests()
    {
        paths = new TestPathProvider(temp);
    }

    public void Dispose() => temp.Dispose();

    private string WriteLegacyState(GameId game, string fileName, string content)
    {
        Directory.CreateDirectory(paths.GetSyncStateRoot(game));
        var path = Path.Combine(paths.GetSyncStateRoot(game), fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private MigrationReport Run(CloudProviderOptions? options = null) =>
        new SyncStateProviderScopeMigrator(paths, options ?? new CloudProviderOptions(), new NullAppLogger())
            .Run([GameId.Windrose, GameId.Dragonwilds]);

    [Fact]
    public void Run_LegacyState_MovesIntoGoogleDriveFolderAndKeepsSharedSubfolders()
    {
        var windrose = WriteLegacyState(GameId.Windrose, "TEST_WORLD_ID.json", "{\"TEST\":1}");
        WriteLegacyState(GameId.Dragonwilds, "TEST_OTHER_WORLD.json", "{}");
        var ledger = Path.Combine(paths.GetSyncStateRoot(GameId.Windrose), "world-files", "TEST_LEDGER.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        File.WriteAllText(ledger, "{}");

        var report = Run();

        Assert.Empty(report.Errors);
        Assert.Equal(2, report.MovedItems);
        Assert.False(File.Exists(windrose));
        Assert.Equal("{\"TEST\":1}", File.ReadAllText(Path.Combine(paths.GetSyncStateRoot(GameId.Windrose, "googledrive"), "TEST_WORLD_ID.json")));
        Assert.True(File.Exists(Path.Combine(paths.GetSyncStateRoot(GameId.Dragonwilds, "googledrive"), "TEST_OTHER_WORLD.json")));
        Assert.True(File.Exists(ledger));
    }

    [Fact]
    public void Run_Twice_SecondRunMovesNothing()
    {
        WriteLegacyState(GameId.Windrose, "TEST_WORLD_ID.json", "{}");

        Run();
        var second = Run();

        Assert.Equal(0, second.MovedItems);
        Assert.Empty(second.Errors);
    }

    [Fact]
    public void Run_TargetExists_NeverOverwrites()
    {
        var legacy = WriteLegacyState(GameId.Windrose, "TEST_WORLD_ID.json", "{\"Legacy\":true}");
        var targetRoot = paths.GetSyncStateRoot(GameId.Windrose, "googledrive");
        Directory.CreateDirectory(targetRoot);
        File.WriteAllText(Path.Combine(targetRoot, "TEST_WORLD_ID.json"), "{\"Current\":true}");

        var report = Run();

        Assert.Equal(0, report.MovedItems);
        Assert.True(File.Exists(legacy));
        Assert.Equal("{\"Current\":true}", File.ReadAllText(Path.Combine(targetRoot, "TEST_WORLD_ID.json")));
    }

    [Fact]
    public void Run_DeveloperOverride_MovesIntoLocalTestFolder()
    {
        WriteLegacyState(GameId.Windrose, "TEST_WORLD_ID.json", "{}");

        Run(new CloudProviderOptions { DeveloperProviderOverride = CloudProviderKind.LocalTest });

        Assert.True(File.Exists(Path.Combine(paths.GetSyncStateRoot(GameId.Windrose, "localtest"), "TEST_WORLD_ID.json")));
    }

    [Fact]
    public async Task SyncState_IsKeptPerProvider()
    {
        var selector = new FixedProviderSelector();
        var service = new LocalJsonSyncStateService(paths, selector);
        var world = new GameWorld(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);
        var driveState = await service.LoadAsync(world, TestContext.Current.CancellationToken);
        driveState.LocalBaseVersionNumber = 7;
        await service.SaveAsync(driveState, TestContext.Current.CancellationToken);

        await selector.SwitchAsync(CloudProviderKind.Supabase, TestContext.Current.CancellationToken);
        var supabaseState = await service.LoadAsync(world, TestContext.Current.CancellationToken);
        await selector.SwitchAsync(CloudProviderKind.GoogleDrive, TestContext.Current.CancellationToken);
        var driveAgain = await service.LoadAsync(world, TestContext.Current.CancellationToken);

        Assert.Null(supabaseState.LocalBaseVersionNumber);
        Assert.Equal(7, driveAgain.LocalBaseVersionNumber);
        Assert.True(File.Exists(Path.Combine(paths.GetSyncStateRoot(GameId.Windrose, "googledrive"), "TEST_WORLD_ID.json")));
    }
}
