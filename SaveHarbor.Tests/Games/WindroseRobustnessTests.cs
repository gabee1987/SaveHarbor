using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games.Windrose;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// Windrose worlds are RocksDB folders whose name must equal the island id inside them; see
// docs/plans/windrose/00-overview.md §1.2 for the findings these tests guard.
public sealed class WindroseRobustnessTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string SettingsJson = """
        {"Version":1,"WorldDescription":{"islandId":"TEST_WORLD_ID","WorldName":"TEST_WORLD","CreationTime":0,"WorldPresetType":"Custom",
         "WorldSettings":{
           "BoolParameters":{"{\"TagName\": \"WDS.Parameter.Coop.SharedQuests\"}":true,"{\"TagName\": \"bad name!\"}":true},
           "FloatParameters":{"{\"TagName\": \"WDS.Parameter.MobHealthMultiplier\"}":0.7,"{\"TagName\": \"WDS.Parameter.TEST_NEW_SETTING\"}":2},
           "TagParameters":{"{\"TagName\": \"WDS.Parameter.CombatDifficulty\"}":{"TagName":"WDS.Parameter.CombatDifficulty.Normal"}}}}}
        """;

    private async Task<GameWorld> WorldAsync(string islandId = "TEST_WORLD_ID", string? description = null)
    {
        var path = SaveFixtures.CreateWindroseWorld(harness.WindroseRoot, "12345", islandId, islandId, "TEST_WORLD");
        if (description is not null)
        {
            File.WriteAllText(Path.Combine(path, WindroseSaveAdapter.DescriptionFileName), description);
        }

        return (await harness.WindroseAdapter.ReadWorldAsync(path, Token))!;
    }

    [Fact]
    public async Task Facts_ShowKnownSettings_AndSkipMalformedTags()
    {
        var world = await WorldAsync(description: SettingsJson);

        var facts = await harness.WindroseAdapter.ReadWorldFactsAsync(world, Token);

        Assert.Contains(facts, fact => fact is { Label: "Combat", Value: "Normal" });
        Assert.Contains(facts, fact => fact is { Label: "Shared quests", Value: "On" });
        Assert.Contains(facts, fact => fact is { Group: WorldFactGroup.Rules, Label: "Enemy health", Value: "×0.7" });
        Assert.Contains(facts, fact => fact is { Label: "Save format", Value: "0.10.0" });

        var settings = await WindroseWorldSettings.ReadAsync(Path.Combine(world.SavePath, WindroseSaveAdapter.DescriptionFileName), Token);
        Assert.DoesNotContain(settings.Flags.Keys, key => key.Contains(' '));
        Assert.Contains("WDS.Parameter.TEST_NEW_SETTING", settings.Multipliers.Keys);
    }

    [Fact]
    public async Task Restore_BackupOfAnotherWorld_IsRefusedAndChangesNothing()
    {
        var other = await WorldAsync("TEST_OTHER_ID");
        var backup = await harness.Backups.CreateBackupAsync(other, BackupReasons.Manual, Token);
        var target = await WorldAsync();
        var before = File.ReadAllText(Path.Combine(target.SavePath, WindroseSaveAdapter.DescriptionFileName));

        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Backups.RestoreBackupAsync(backup.FilePath, target, Token));

        Assert.Equal(before, File.ReadAllText(Path.Combine(target.SavePath, WindroseSaveAdapter.DescriptionFileName)));
    }

    [Fact]
    public async Task Backup_RecordsSaveFormat_AndANewerFormatIsNeverRestored()
    {
        var world = await WorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);
        Assert.Equal("0.10.0", (await harness.Backups.ReadManifestAsync(backup.FilePath, GameId.Windrose, Token)).SaveFormatVersion);

        var newer = Path.Combine(harness.TempPath, "TEST_NEWER.zip");
        File.Copy(backup.FilePath, newer);
        ZipManifestEditor.Update(newer, manifest => manifest.SaveFormatVersion = "0.11.0");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Backups.RestoreBackupAsync(newer, world, Token));
        Assert.Contains("newer game version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_LeavesNoStagingFoldersNextToTheWorld()
    {
        var world = await WorldAsync();
        var backup = await harness.Backups.CreateBackupAsync(world, BackupReasons.Manual, Token);

        await harness.Backups.RestoreBackupAsync(backup.FilePath, world, Token);

        var worldsFolder = Path.GetDirectoryName(world.SavePath)!;
        Assert.Equal([world.SavePath], Directory.GetDirectories(worldsFolder));
    }

    [Fact]
    public async Task Discovery_IgnoresFoldersLeftByAnInterruptedRestore()
    {
        var world = await WorldAsync();
        var leftover = world.SavePath + ".saveharbor-prev-TEST";
        Directory.CreateDirectory(leftover);
        File.Copy(Path.Combine(world.SavePath, WindroseSaveAdapter.DescriptionFileName), Path.Combine(leftover, WindroseSaveAdapter.DescriptionFileName));

        var worlds = await harness.WindroseAdapter.DiscoverWorldsAsync(Token);

        Assert.Equal(world.SavePath, Assert.Single(worlds).SavePath);
    }

    [Fact]
    public async Task Inspector_CountsLocalCharacters_ButNeverReadsThem()
    {
        var world = await WorldAsync();
        Directory.CreateDirectory(WindrosePaths.SiblingDatabase(world.SavePath, WindrosePaths.PlayersFolderName)! + "\\TEST_PLAYER");

        var sections = await harness.WindroseAdapter.InspectWorldAsync(world, Token);

        var local = Assert.Single(sections, section => section.Title.StartsWith("This PC only", StringComparison.Ordinal));
        Assert.Contains(local.Items, item => item is { Label: "Characters on this PC", Value: "1" });
    }
}
