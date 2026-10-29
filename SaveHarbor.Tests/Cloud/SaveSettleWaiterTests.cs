using System.Reflection;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
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

        // The file changes between every two looks, so the result does not depend on how busy the machine is.
        var settled = await SaveSettleWaiter.WaitAsync(StillWritingAdapter.For(path), world, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1500), Token);

        Assert.False(settled);
    }

    // An adapter whose world file gets a newer write time each time its files are listed, like a game still saving.
    public class StillWritingAdapter : DispatchProxy
    {
        private string path = string.Empty;
        private int tick;

        public static IGameSaveAdapter For(string path)
        {
            var adapter = Create<IGameSaveAdapter, StillWritingAdapter>();
            ((StillWritingAdapter)(object)adapter).path = path;
            return adapter;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IGameSaveAdapter.GetPayloadFiles))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(++tick));
            return new[] { path };
        }
    }
}
