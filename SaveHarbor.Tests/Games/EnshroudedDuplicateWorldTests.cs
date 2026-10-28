using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

// One GUID in two slots makes the game list the world twice, and which copy holds the latest progress is no longer
// clear, so both copies carry a notice until the extras are deleted in the game.
public sealed class EnshroudedDuplicateWorldTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private async Task<GameWorld> WriteAsync(string fileName, string worldId, uint saveCounter = 5) =>
        await harness.ReadEnshroudedWorldAsync(EnshroudedSaveBuilder.Write(harness.EnshroudedRoot, fileName, worldId, saveCounter));

    private IReadOnlyList<SaveHealthNotice> Check(GameWorld world) => harness.EnshroudedAdapter.CheckHealth(world)!;

    [Fact]
    public async Task CheckHealth_DifferentWorldsInTheFolder_ReportsNothing()
    {
        var first = await WriteAsync("world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var second = await WriteAsync("world1.ksd", EnshroudedSaveBuilder.WorldIdTwo);

        Assert.Empty(Check(first));
        Assert.Empty(Check(second));
    }

    [Fact]
    public async Task CheckHealth_SameWorldInTwoSlots_ReportsBothCopiesWithTheGameSlots()
    {
        var first = await WriteAsync("world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var second = await WriteAsync("world3.ksd", EnshroudedSaveBuilder.WorldIdOne);

        var onFirst = Assert.Single(Check(first));
        var onSecond = Assert.Single(Check(second));

        Assert.Equal(SaveHealthIssue.DuplicateWorld, onFirst.Issue);
        Assert.Equal(SaveHealthIssue.DuplicateWorld, onSecond.Issue);
        Assert.Contains("Slot 1 and slot 4", onFirst.Detail, StringComparison.Ordinal);
        Assert.Contains("Slot 4 and slot 1", onSecond.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealth_OlderDuplicate_ReportsTheDuplicateFirst()
    {
        var newer = await WriteAsync("world0.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 9);
        Assert.Empty(Check(newer));
        var older = await WriteAsync("world2.ksd", EnshroudedSaveBuilder.WorldIdOne, saveCounter: 7);

        var notices = Check(older);

        Assert.Equal([SaveHealthIssue.DuplicateWorld, SaveHealthIssue.WorldReplaced], notices.Select(notice => notice.Issue));
    }

    [Fact]
    public async Task CheckHealth_ExtraCopyDeleted_ReportsNothing()
    {
        var first = await WriteAsync("world0.ksd", EnshroudedSaveBuilder.WorldIdOne);
        var second = await WriteAsync("world1.ksd", EnshroudedSaveBuilder.WorldIdOne);
        Assert.Single(Check(first));

        File.Delete(second.SavePath);

        Assert.Empty(Check(first));
    }
}
