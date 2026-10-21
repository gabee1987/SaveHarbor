using System.Security.Cryptography;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace SaveHarbor.Tests.Support;

// An update feed in memory: the releases it lists and the package bytes it serves.
public sealed class FakeUpdateSource : IUpdateSource
{
    private readonly Dictionary<string, byte[]> packages = new(StringComparer.OrdinalIgnoreCase);

    public List<VelopackAsset> Releases { get; } = [];

    // Thrown by the next feed request, like an offline PC or GitHub's rate limit.
    public Exception? FeedFailure { get; set; }

    public int FeedRequests { get; private set; }

    public int Downloads { get; private set; }

    public VelopackAsset AddFullRelease(string version, string notes = "")
    {
        var bytes = RandomNumberGenerator.GetBytes(2048);
        var release = new VelopackAsset
        {
            PackageId = "SaveHarborApp",
            Version = SemanticVersion.Parse(version),
            Type = VelopackAssetType.Full,
            FileName = $"SaveHarborApp-{version}-full.nupkg",
            SHA1 = Convert.ToHexString(SHA1.HashData(bytes)),
            SHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Size = bytes.Length,
            NotesMarkdown = notes
        };
        packages[release.FileName] = bytes;
        Releases.Add(release);
        return release;
    }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        FeedRequests++;
        if (FeedFailure is { } failure)
        {
            FeedFailure = null;
            throw failure;
        }

        return Task.FromResult(new VelopackAssetFeed { Assets = [.. Releases] });
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
    {
        Downloads++;
        progress(50);
        await File.WriteAllBytesAsync(localFile, packages[releaseEntry.FileName], cancelToken);
        progress(100);
    }
}
