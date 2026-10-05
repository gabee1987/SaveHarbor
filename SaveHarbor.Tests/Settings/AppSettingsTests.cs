using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Settings;

public sealed class AppSettingsTests
{
    [Fact]
    public void Update_NewValues_ArePersistedForNextInstance()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        new AppSettingsStore(paths).Update(settings =>
        {
            settings.PlayerName = "TEST_USER";
            settings.BackupRetentionCount = 10;
            settings.SaveRootOverrides["DRAGONWILDS"] = "C:\\TEST_PATH";
        });

        var restored = new AppSettingsStore(paths).Current;

        Assert.Equal("TEST_USER", restored.PlayerName);
        Assert.Equal(10, restored.BackupRetentionCount);
        Assert.Equal("C:\\TEST_PATH", restored.SaveRootOverrides["dragonwilds"]);
    }

    [Fact]
    public void Update_LastGame_KeepsOtherSettings()
    {
        using var temp = new TempDirectory();
        var paths = new TestPathProvider(temp);
        var store = new AppSettingsStore(paths);
        store.Update(settings => settings.PlayerName = "TEST_USER");

        var context = new ActiveGameContext(new StubGameRegistry(new StubGameDefinition(GameId.Windrose), new StubGameDefinition(GameId.Dragonwilds)), store, null);
        context.SetActive(GameId.Dragonwilds);

        Assert.Equal("TEST_USER", new AppSettingsStore(paths).Current.PlayerName);
    }

    [Fact]
    public void PlayerIdentity_EmptyName_FallsBackToWindowsUserName()
    {
        using var temp = new TempDirectory();

        var identity = new PlayerIdentity(new AppSettingsStore(new TestPathProvider(temp)));

        Assert.Equal(Environment.UserName, identity.DisplayName);
    }

    [Fact]
    public void PlayerIdentity_Normalize_RemovesControlCharactersAndLimitsLength()
    {
        var normalized = PlayerIdentity.Normalize("  TEST\u0007_USER" + new string('x', 60));

        Assert.StartsWith("TEST_USER", normalized);
        Assert.Equal(PlayerIdentity.MaxLength, normalized.Length);
    }

    [Fact]
    public void GameOptionsProvider_SaveRootOverride_WinsOverConfiguredRoot()
    {
        using var temp = new TempDirectory();
        var store = new AppSettingsStore(new TestPathProvider(temp));
        store.Update(settings => settings.SaveRootOverrides["dragonwilds"] = "C:\\TEST_OVERRIDE");
        var provider = new GameOptionsProvider(
            new Dictionary<GameId, GameOptions> { [GameId.Dragonwilds] = new() { SaveRoot = "C:\\TEST_CONFIGURED", LaunchUri = "steam://rungameid/12345" } },
            store);

        var options = provider.Get(GameId.Dragonwilds);

        Assert.Equal("C:\\TEST_OVERRIDE", options.SaveRoot);
        Assert.Equal("steam://rungameid/12345", options.LaunchUri);
    }

    [Fact]
    public async Task CreateBackup_RetentionLimit_KeepsOnlyNewestBackups()
    {
        using var harness = new BackupHarness();
        harness.Settings.Update(settings => settings.BackupRetentionCount = 2);
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));

        BackupInfo? newest = null;
        for (var index = 0; index < 3; index++)
        {
            newest = await harness.Backups.CreateBackupAsync(world, $"manual{index}", TestContext.Current.CancellationToken);
        }

        var remaining = await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken);
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, backup => backup.FilePath == newest!.FilePath);
    }

    [Fact]
    public async Task CreateBackup_NoRetentionLimit_KeepsEveryBackup()
    {
        using var harness = new BackupHarness();
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));

        for (var index = 0; index < 3; index++)
        {
            await harness.Backups.CreateBackupAsync(world, $"manual{index}", TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, (await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken)).Count);
    }
}
