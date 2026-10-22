using System.Security.Cryptography;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Migrations;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Migrations;

public sealed class LegacyLayoutMigratorTests
{
    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CreateLegacyFixture(TestPathProvider paths)
    {
        Directory.CreateDirectory(paths.LegacyBackupRoot);
        File.WriteAllText(Path.Combine(paths.LegacyBackupRoot, "one.zip"), "zip-one");
        File.WriteAllText(Path.Combine(paths.LegacyBackupRoot, "two.zip"), "zip-two");
        Directory.CreateDirectory(paths.LegacySyncStateRoot);
        File.WriteAllText(Path.Combine(paths.LegacySyncStateRoot, "TEST_WORLD_ID.json"), "{}");
        var worldFolder = Path.Combine(paths.LegacyLocalTestCloudRoot, "worlds", "TEST_WORLD_ID");
        Directory.CreateDirectory(worldFolder);
        File.WriteAllText(Path.Combine(worldFolder, "manifest.json"), "{}");
    }

    [Fact]
    public void Run_LegacyLayout_MovesFilesToWindroseLocations()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        CreateLegacyFixture(paths);
        var zipHash = Sha256(Path.Combine(paths.LegacyBackupRoot, "one.zip"));

        var report = new LegacyLayoutMigrator(paths, new NullAppLogger()).Run();

        Assert.Empty(report.Errors);
        Assert.Equal(4, report.MovedItems);
        Assert.False(File.Exists(Path.Combine(paths.LegacyBackupRoot, "one.zip")));
        Assert.Equal(zipHash, Sha256(Path.Combine(paths.GetBackupRoot(GameId.Windrose), "one.zip")));
        Assert.True(File.Exists(Path.Combine(paths.GetSyncStateRoot(GameId.Windrose), "TEST_WORLD_ID.json")));
        Assert.True(File.Exists(Path.Combine(paths.GetLocalTestCloudRoot(GameId.Windrose), "worlds", "TEST_WORLD_ID", "manifest.json")));
    }

    [Fact]
    public void Run_SecondRun_MovesNothing()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        CreateLegacyFixture(paths);
        var migrator = new LegacyLayoutMigrator(paths, new NullAppLogger());
        migrator.Run();

        var second = migrator.Run();

        Assert.Equal(0, second.MovedItems);
    }

    [Fact]
    public void Run_TargetAlreadyExists_SkipsAndKeepsSource()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        CreateLegacyFixture(paths);
        Directory.CreateDirectory(paths.GetBackupRoot(GameId.Windrose));
        File.WriteAllText(Path.Combine(paths.GetBackupRoot(GameId.Windrose), "one.zip"), "existing");

        var report = new LegacyLayoutMigrator(paths, new NullAppLogger()).Run();

        Assert.Empty(report.Errors);
        Assert.Equal("zip-one", File.ReadAllText(Path.Combine(paths.LegacyBackupRoot, "one.zip")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(paths.GetBackupRoot(GameId.Windrose), "one.zip")));
    }

    [Fact]
    public void Run_NoErrors_WritesMarker()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        CreateLegacyFixture(paths);

        new LegacyLayoutMigrator(paths, new NullAppLogger()).Run();

        Assert.Contains("LegacyLayoutV1", File.ReadAllText(Path.Combine(paths.AppDataRoot, "migrations.json")));
    }

    [Fact]
    public void Run_LockedSourceFile_ReportsErrorAndWritesNoMarker()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        CreateLegacyFixture(paths);
        using var locked = new FileStream(Path.Combine(paths.LegacyBackupRoot, "one.zip"), FileMode.Open, FileAccess.Read, FileShare.None);

        var report = new LegacyLayoutMigrator(paths, new NullAppLogger()).Run();

        Assert.NotEmpty(report.Errors);
        Assert.False(File.Exists(Path.Combine(paths.AppDataRoot, "migrations.json")));
    }
}
