using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

// The cached overview comes back only for its own group, and keeps only what the window shows.
public sealed class CloudSnapshotCacheTests : IDisposable
{
    private static readonly DateTimeOffset CachedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory temp = new();
    private readonly CloudSnapshotCache cache;

    public CloudSnapshotCacheTests()
    {
        cache = new CloudSnapshotCache(new TestPathProvider(temp), new NullAppLogger());
    }

    public void Dispose() => temp.Dispose();

    private static CloudWorldOverview Overview() => new(
        true,
        [new WorldSyncSummary("TEST_WORLD_A", CloudSyncState.CloudNewer, false, 1, Version(2))],
        [new CloudWorldManifest { Game = "dragonwilds", WorldId = "TEST_WORLD_B", WorldName = "TEST_WORLD_B", LatestVersion = Version(4) }]);

    private static CloudVersionMetadata Version(int number) => new()
    {
        VersionNumber = number,
        VersionId = $"v{number}",
        UploadedBy = "TEST_USER",
        UploaderMachine = "TEST_DEVICE",
        ArchiveSha256 = "TEST_HASH",
        UploadedAtUtc = CachedAt
    };

    [Fact]
    public void Load_SameScope_ReturnsTheOverviewAndItsTime()
    {
        cache.Save("supabase", GameId.Dragonwilds, "TEST_GROUP", Overview(), CachedAt);

        var cached = cache.Load("supabase", GameId.Dragonwilds, "TEST_GROUP");

        Assert.NotNull(cached);
        Assert.Equal(CachedAt, cached.CachedAtUtc);
        var summary = Assert.Single(cached.Overview.LocalWorlds);
        Assert.Equal(CloudSyncState.CloudNewer, summary.State);
        Assert.Equal(2, summary.LatestVersion!.VersionNumber);
        Assert.Equal("TEST_USER", summary.LatestVersion.UploadedBy);
        Assert.Equal("TEST_WORLD_B", Assert.Single(cached.Overview.CloudOnlyWorlds).WorldName);
    }

    [Fact]
    public void Load_OtherGroupOrGame_ReturnsNothing()
    {
        cache.Save("supabase", GameId.Dragonwilds, "TEST_GROUP", Overview(), CachedAt);

        Assert.Null(cache.Load("supabase", GameId.Dragonwilds, "TEST_GROUP_2"));
        Assert.Null(cache.Load("supabase", GameId.Windrose, "TEST_GROUP"));
        Assert.Null(cache.Load("googledrive", GameId.Dragonwilds, "TEST_GROUP"));
    }

    [Fact]
    public void Save_KeepsNoMachineNamesOrHashes()
    {
        cache.Save("supabase", GameId.Dragonwilds, "TEST_GROUP", Overview(), CachedAt);

        var json = File.ReadAllText(Directory.GetFiles(temp.Combine("appdata", "cloud-cache"), "*.json", SearchOption.AllDirectories).Single());

        Assert.DoesNotContain("TEST_DEVICE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST_HASH", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_DeletesTheCachedOverview()
    {
        cache.Save("supabase", GameId.Dragonwilds, "TEST_GROUP", Overview(), CachedAt);

        cache.Remove("supabase", GameId.Dragonwilds);

        Assert.Null(cache.Load("supabase", GameId.Dragonwilds, "TEST_GROUP"));
    }

    [Fact]
    public void Load_DamagedFile_ReturnsNothing()
    {
        var path = temp.Combine("appdata", "cloud-cache", "supabase", "dragonwilds.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(cache.Load("supabase", GameId.Dragonwilds, "TEST_GROUP"));
    }
}
