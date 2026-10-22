using SaveHarbor.App.Domain;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.Tests.ViewModels;

// The toasts after a live refresh: what friends did between two overviews, without this PC's own actions.
public sealed class CloudFriendActivityTests
{
    private const string OwnDevice = "TEST_DEVICE";

    [Fact]
    public void AFriendsNewVersionOfALocalWorld_IsReported()
    {
        var before = Overview(local: [Summary("TEST_WORLD_ID", latest: 4, localBase: 4)]);
        var after = Overview(local: [Summary("TEST_WORLD_ID", latest: 5, localBase: 4, by: "TEST_FRIEND")]);

        var activity = Assert.Single(Compare(before, after));

        Assert.Equal("TEST_FRIEND shared v5 of Test World.", activity.Message);
    }

    [Fact]
    public void OwnUpload_IsNotReported()
    {
        var before = Overview(local: [Summary("TEST_WORLD_ID", latest: 4, localBase: 4)]);
        var after = Overview(local: [Summary("TEST_WORLD_ID", latest: 5, localBase: 5, by: "TEST_USER")]);

        Assert.Empty(Compare(before, after));
    }

    [Fact]
    public void ANewWorldOnlyInTheCloud_IsReported()
    {
        var after = Overview(cloudOnly: [Manifest("TEST_NEW_WORLD", latest: 1, by: "TEST_FRIEND")]);

        var activity = Assert.Single(Compare(Overview(), after));

        Assert.Equal("TEST_FRIEND shared v1 of Test World.", activity.Message);
    }

    [Fact]
    public void AFriendStartingAndFinishing_IsReported()
    {
        var idle = Overview();
        var playing = Overview(locks: new() { ["TEST_WORLD_ID"] = Lock("lock-1", "TEST_OTHER_DEVICE", "TEST_FRIEND") });

        Assert.Equal("TEST_FRIEND started playing Test World.", Assert.Single(Compare(idle, playing)).Message);
        Assert.Equal("TEST_FRIEND finished playing Test World.", Assert.Single(Compare(playing, idle)).Message);
    }

    [Fact]
    public void OwnSession_AndAnUnchangedLock_AreNotReported()
    {
        var own = Overview(locks: new() { ["TEST_WORLD_ID"] = Lock("lock-1", OwnDevice, "TEST_USER") });
        var friend = Overview(locks: new() { ["TEST_WORLD_ID"] = Lock("lock-2", "TEST_OTHER_DEVICE", "TEST_FRIEND") });

        Assert.Empty(Compare(Overview(), own));
        Assert.Empty(Compare(own, Overview()));
        Assert.Empty(Compare(friend, friend));
    }

    [Fact]
    public void ATakeOver_ReportsBothPlayers()
    {
        var first = Overview(locks: new() { ["TEST_WORLD_ID"] = Lock("lock-1", "TEST_OTHER_DEVICE", "TEST_FRIEND") });
        var second = Overview(locks: new() { ["TEST_WORLD_ID"] = Lock("lock-2", "TEST_THIRD_DEVICE", "TEST_FRIEND_2") });

        Assert.Equal(
            ["TEST_FRIEND finished playing Test World.", "TEST_FRIEND_2 started playing Test World."],
            Compare(first, second).Select(activity => activity.Message));
    }

    private static IReadOnlyList<FriendActivity> Compare(CloudWorldOverview before, CloudWorldOverview after) =>
        CloudFriendActivity.Compare(before, after, OwnDevice, _ => "Test World");

    private static CloudWorldOverview Overview(
        WorldSyncSummary[]? local = null,
        CloudWorldManifest[]? cloudOnly = null,
        Dictionary<string, CloudSessionLock>? locks = null) =>
        new(true, local ?? [], cloudOnly ?? [], locks ?? []);

    private static WorldSyncSummary Summary(string worldId, int latest, int? localBase, string by = "TEST_USER") =>
        new(worldId, CloudSyncState.UpToDate, false, localBase, Version(latest, by));

    private static CloudWorldManifest Manifest(string worldId, int latest, string by) =>
        new() { WorldId = worldId, WorldName = "Test World", LatestVersion = Version(latest, by) };

    private static CloudVersionMetadata Version(int number, string by) =>
        new() { VersionNumber = number, VersionId = $"v{number}", UploadedBy = by };

    private static CloudSessionLock Lock(string lockId, string deviceId, string player) =>
        new() { LockId = lockId, DeviceId = deviceId, PlayerName = player, WorldId = "TEST_WORLD_ID" };
}
