using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// A game may still write its save for a few seconds after its process is gone. Waits until the world's files keep
// their last write time for a while, so the upload after the game closed copies the finished save.
public static class SaveSettleWaiter
{
    public static readonly TimeSpan StableFor = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    // False when the files were still changing at maxWait; the caller goes on with what is there.
    public static async Task<bool> WaitAsync(
        IGameSaveAdapter adapter,
        GameWorld world,
        TimeSpan stableFor,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var lastWrite = ReadLastWrite(adapter, world);
        var stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow - stableSince < stableFor)
        {
            if (DateTime.UtcNow - started >= maxWait)
            {
                return false;
            }

            await Task.Delay(stableFor < Poll ? stableFor : Poll, cancellationToken);
            var current = ReadLastWrite(adapter, world);
            if (current != lastWrite)
            {
                lastWrite = current;
                stableSince = DateTime.UtcNow;
            }
        }

        return true;
    }

    // The newest write time of the world's files; null while they cannot be read (the game may still hold them).
    private static DateTime? ReadLastWrite(IGameSaveAdapter adapter, GameWorld world)
    {
        try
        {
            var files = adapter.GetPayloadFiles(world);
            return files.Count == 0 ? null : files.Max(File.GetLastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
