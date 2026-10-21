using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// Rows from the group's cloud become manifests, locks and members; names that could reach a path are checked.
public sealed class SupabaseMappingTests
{
    private static SnapshotDto Snapshot() =>
        SupabaseHttp.Deserialize<SnapshotDto>(RecordingHttpMessageHandler.Fixture("snapshot.json"));

    [Fact]
    public void Snapshot_MapsWorldsLocksMembersAndServerTime()
    {
        var snapshot = SupabaseMapping.ToSnapshot(Snapshot(), "windrose");

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 30, 0, TimeSpan.Zero), snapshot.ServerTimeUtc);
        Assert.Equal(["TEST_USER", "TEST_FRIEND"], snapshot.Members.Select(member => member.DisplayName));

        var world = snapshot.Manifests.Single(manifest => manifest.WorldId == "TEST_WORLD_ID");
        Assert.Equal("Supabase", world.Provider);
        Assert.Equal("windrose", world.Game);
        Assert.Equal(10, world.Retention.KeepLatestVersions);
        var latest = world.LatestVersion!;
        Assert.Equal(1, latest.VersionNumber);
        Assert.Equal(SupabaseHarness.VersionId + ".zip", latest.ArchiveFileName);
        Assert.Equal("TEST_USER", latest.UploadedBy);
        Assert.Equal("TEST_DEVICE", latest.UploaderMachine);
        Assert.Equal(12345, latest.ArchiveSizeBytes);

        var sessionLock = snapshot.LocksByWorldId["TEST_WORLD_ID"];
        Assert.Equal("TEST_FRIEND", sessionLock.PlayerName);
        Assert.Equal("TEST_FRIEND_PC", sessionLock.MachineName);
        Assert.Equal("77777777-7777-4777-8777-777777777777", sessionLock.DeviceId);
        Assert.Equal("Playing", sessionLock.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 45, 0, TimeSpan.Zero), sessionLock.ExpiresAtUtc);
    }

    [Fact]
    public void Snapshot_WorldWithoutVersion_HasNullLatestAndNoLock()
    {
        var snapshot = SupabaseMapping.ToSnapshot(Snapshot(), "windrose");

        var empty = snapshot.Manifests.Single(manifest => manifest.WorldId == "TEST_EMPTY_WORLD");
        Assert.Null(empty.LatestVersion);
        Assert.False(snapshot.LocksByWorldId.ContainsKey("TEST_EMPTY_WORLD"));
    }

    [Fact]
    public void Snapshot_WorldWithUnsafeId_IsSkippedAndReported()
    {
        var skipped = new List<string>();

        var snapshot = SupabaseMapping.ToSnapshot(Snapshot(), "windrose", skipped.Add);

        Assert.DoesNotContain(snapshot.Manifests, manifest => manifest.WorldId == "..");
        Assert.Equal(["99999999-9999-4999-8999-999999999999"], skipped);
    }

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111/windrose/OTHER_WORLD/20261020_120000_TEST_USER_v1.zip")]
    [InlineData("11111111-1111-4111-8111-111111111111/dragonwilds/TEST_WORLD_ID/20261020_120000_TEST_USER_v1.zip")]
    [InlineData("22222222-2222-4222-8222-222222222222/windrose/TEST_WORLD_ID/20261020_120000_TEST_USER_v1.zip")]
    [InlineData("11111111-1111-4111-8111-111111111111/windrose/TEST_WORLD_ID/other.zip")]
    public void Version_ArchiveOutsideItsWorld_IsRejected(string archivePath)
    {
        var version = Snapshot().Worlds[0].Latest!;
        version.ArchivePath = archivePath;

        var exception = Assert.Throws<SupabaseException>(() => SupabaseMapping.ToVersion(version, "windrose", "TEST_WORLD_ID"));
        Assert.Equal(SupabaseErrorCodes.InvalidData, exception.Code);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("name with/slash")]
    [InlineData("")]
    public void Version_InvalidVersionId_IsRejected(string versionId)
    {
        var version = Snapshot().Worlds[0].Latest!;
        version.VersionId = versionId;

        Assert.Throws<SupabaseException>(() => SupabaseMapping.ToVersion(version, "windrose", "TEST_WORLD_ID"));
    }

    [Theory]
    [InlineData("TEST_WORLD_ID", true)]
    [InlineData("World (2) v1.0", true)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("CON", false)]
    [InlineData("Wörld", false)]
    [InlineData(" leading", false)]
    public void WorldId_FollowsTheDatabaseRules(string worldId, bool valid)
    {
        Assert.Equal(valid, SupabaseMapping.IsValidWorldId(worldId));
    }

    [Fact]
    public void Group_WithoutUuid_IsRejected()
    {
        Assert.Throws<SupabaseException>(() => SupabaseMapping.ToGroup(new GroupSummaryDto { Id = "TEST_GROUP" }));
    }

    [Fact]
    public void Group_WithoutRole_CountsAsMember()
    {
        var group = SupabaseMapping.ToGroup(new GroupSummaryDto { Id = SupabaseHarness.GroupId, Name = "TEST_GROUP", MemberCount = 2 });

        Assert.Equal("member", group.MyRole);
        Assert.False(group.IsOwner);
    }
}
