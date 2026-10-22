using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// The upload after the game closed waits until the save stops changing, but not forever.
public sealed class SaveSettleWaiterTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Wait_FilesUnchanged_Settles()
    {
        var world = await harness.ReadDragonwildsWorldAsync(harness.WriteDragonwildsWorld("TEST_WORLD.sav"));

        var settled = await SaveSettleWaiter.WaitAsync(harness.DragonwildsAdapter, world, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), Token);

        Assert.True(settled);
    }

    [Fact]
    public async Task Wait_FilesKeepChanging_GivesUpAtTheLimit()
    {
        var path = harness.WriteDragonwildsWorld("TEST_WORLD.sav");
        var world = await harness.ReadDragonwildsWorldAsync(path);
        using var writing = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            var tick = 0;
            while (!writing.IsCancellationRequested)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(++tick));
                await Task.Delay(50, CancellationToken.None);
            }
        }, Token);

        var settled = await SaveSettleWaiter.WaitAsync(harness.DragonwildsAdapter, world, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1500), Token);
        await writing.CancelAsync();
        await writer;

        Assert.False(settled);
    }
}
