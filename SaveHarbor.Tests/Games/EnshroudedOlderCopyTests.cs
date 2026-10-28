using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// The save counter only rises, whoever hosts, so a lower counter than this PC has seen means an older copy of the world
// came back (Steam Cloud, a Steam conflict, a copy by hand). That copy must not be shared as new progress.
public sealed class EnshroudedOlderCopyTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string WorldPath => Path.Combine(harness.EnshroudedRoot, "world1.ksd");

    private async Task<GameWorld> SaveAsync(uint saveCounter)
    {
        EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, "world1.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter);
        return await harness.ReadEnshroudedWorldAsync(WorldPath);
    }

    private IReadOnlyList<SaveHealthNotice> Check(GameWorld world) => harness.EnshroudedAdapter.CheckHealth(world)!;

    [Fact]
    public async Task CheckHealth_CounterOnlyRises_ReportsNothing()
    {
        Assert.Empty(Check(await SaveAsync(saveCounter: 5)));
        Assert.Empty(Check(await SaveAsync(saveCounter: 5)));
        Assert.Empty(Check(await SaveAsync(saveCounter: 9)));
    }

    [Fact]
    public async Task CheckHealth_CounterGoesDown_ReportsAnOlderCopy()
    {
        Check(await SaveAsync(saveCounter: 9));

        var notice = Assert.Single(Check(await SaveAsync(saveCounter: 7)));

        Assert.Equal(SaveHealthIssue.WorldReplaced, notice.Issue);
        Assert.Contains("7", notice.Detail, StringComparison.Ordinal);
        Assert.Contains("9", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealth_OlderCopyStaysReportedUntilAccepted()
    {
        Check(await SaveAsync(saveCounter: 9));
        var older = await SaveAsync(saveCounter: 7);
        Assert.Single(Check(older));
        Assert.Single(Check(older));

        harness.EnshroudedAdapter.RememberWorldState(older.SavePath);

        Assert.Empty(Check(older));
        Assert.Empty(Check(await SaveAsync(saveCounter: 8)));
    }

    // Restoring an older SaveHarbor backup is the player's own choice; it is the new baseline, not a warning.
    [Fact]
    public async Task Restore_OlderBackup_BecomesTheBaseline()
    {
        var early = await SaveAsync(saveCounter: 3);
        var backup = await harness.Backups.CreateBackupAsync(early, "manual", Token);
        var later = await SaveAsync(saveCounter: 12);
        Assert.Empty(Check(later));

        await harness.Backups.RestoreBackupAsync(backup.FilePath, later, Token);

        Assert.Empty(Check(await harness.ReadEnshroudedWorldAsync(WorldPath)));
    }

    [Fact]
    public async Task Import_SharedVersion_BecomesTheBaseline()
    {
        var source = await harness.ReadEnshroudedWorldAsync(
            EnshroudedSaveBuilder.Write(Path.Combine(harness.TempPath, "other-pc"), "world4.ksd", EnshroudedSaveBuilder.WorldIdTwo, saveCounter: 40));
        var backup = await harness.Backups.CreateBackupAsync(source, "manual", Token);

        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(backup.FilePath, await harness.EnshroudedProfileAsync(), overwriteExisting: false, Token);
        EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, Path.GetFileName(imported), EnshroudedSaveBuilder.WorldIdTwo, saveCounter: 39);

        Assert.Single(Check(await harness.ReadEnshroudedWorldAsync(imported)));
    }
}
