using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.App.Infrastructure.Games.Windrose;

namespace SaveHarbor.Tests.Support;

public sealed class BackupHarness : IDisposable
{
    private readonly TempDirectory temp = new();

    public BackupHarness()
    {
        WindroseRoot = temp.Combine("windrose-profiles");
        DragonwildsRoot = temp.Combine("dragonwilds-saves");
        Directory.CreateDirectory(DragonwildsRoot);

        var options = new GameOptionsProvider(new Dictionary<GameId, GameOptions>
        {
            [GameId.Windrose] = new() { SaveRoot = WindroseRoot },
            [GameId.Dragonwilds] = new() { SaveRoot = DragonwildsRoot }
        });

        Paths = new TestPathProvider(temp);
        WindroseAdapter = new WindroseSaveAdapter(options, Paths);
        DragonwildsAdapter = new DragonwildsSaveAdapter(options);
        Settings = new AppSettingsStore(Paths);
        Backups = new ZipBackupService(Paths, new StubGameRegistry(
            new StubGameDefinition(GameId.Windrose, WindroseAdapter),
            new StubGameDefinition(GameId.Dragonwilds, DragonwildsAdapter)), Settings);
    }

    public string WindroseRoot { get; }

    public string DragonwildsRoot { get; }

    public WindroseSaveAdapter WindroseAdapter { get; }

    public DragonwildsSaveAdapter DragonwildsAdapter { get; }

    public TestPathProvider Paths { get; }

    public AppSettingsStore Settings { get; }

    public ZipBackupService Backups { get; }

    public string TempPath => temp.Path;

    public string WriteDragonwildsWorld(string fileName, int sizeBytes = 4096)
    {
        var path = Path.Combine(DragonwildsRoot, fileName);
        var bytes = new byte[sizeBytes];
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public async Task<GameWorld> ReadDragonwildsWorldAsync(string path) =>
        (await DragonwildsAdapter.ReadWorldAsync(path, TestContext.Current.CancellationToken))!;

    public void Dispose() => temp.Dispose();
}
