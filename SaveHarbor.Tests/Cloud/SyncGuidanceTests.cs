using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.Tests.Cloud;

public sealed class SyncGuidanceTests
{
    [Theory]
    [InlineData(null, null, false, false, CloudSyncState.ConnectedNoCloudSave)]
    [InlineData(3, 3, false, false, CloudSyncState.UpToDate)]
    [InlineData(3, 3, true, false, CloudSyncState.LocalNewerUploadSafe)]
    [InlineData(null, 3, false, false, CloudSyncState.CloudNewer)]
    [InlineData(2, 3, false, false, CloudSyncState.CloudNewer)]
    [InlineData(2, 3, true, false, CloudSyncState.Conflict)]
    [InlineData(4, 3, false, false, CloudSyncState.Conflict)]
    [InlineData(3, 3, true, true, CloudSyncState.SomeonePlaying)]
    public void Classify_CoversEveryCombination(int? localBase, int? latest, bool changed, bool otherPlaying, CloudSyncState expected)
    {
        Assert.Equal(expected, SyncStateClassifier.Classify(localBase, latest, changed, otherPlaying));
    }

    private static CloudSyncStatus Status(CloudSyncState state, int? localBase, int? latest, bool changed = false) => new(
        state,
        new CloudConnectionStatus(true, "TEST_PROVIDER", "TEST_USER", "Connected."),
        null,
        latest is null ? null : new CloudVersionMetadata { VersionNumber = latest.Value, UploadedBy = "TEST_USER", UploadedAtUtc = DateTimeOffset.UtcNow },
        null,
        new LocalSyncState { WorldId = "TEST_WORLD", LocalBaseVersionNumber = localBase },
        "TEST_TITLE",
        "TEST_DETAIL")
    {
        HasLocalChanges = changed
    };

    [Theory]
    [InlineData(CloudSyncState.LocalNewerUploadSafe, 3, 3, true, SyncAction.Upload, "→")]
    [InlineData(CloudSyncState.ConnectedNoCloudSave, null, null, false, SyncAction.Upload, "→")]
    [InlineData(CloudSyncState.CloudNewer, 2, 3, false, SyncAction.Download, "←")]
    [InlineData(CloudSyncState.Conflict, 2, 3, true, SyncAction.Download, "≠")]
    [InlineData(CloudSyncState.UpToDate, 3, 3, false, SyncAction.None, "=")]
    public void Advise_RecommendsTheActionThatResolvesTheState(CloudSyncState state, int? localBase, int? latest, bool changed, SyncAction action, string direction)
    {
        var advice = SyncAdvisor.Advise(Status(state, localBase, latest, changed));

        Assert.Equal(action, advice.Action);
        Assert.Equal(direction, advice.Direction);
        Assert.Equal(action != SyncAction.None, advice.HasAction);
    }

    [Fact]
    public void DescribeLocal_PlayedSinceBase_SaysNotUploaded()
    {
        var world = new GameWorld(GameId.Dragonwilds, "TEST_WORLD", "TEST_WORLD", string.Empty, "C:\\TEST_PATH", DateTimeOffset.Now, DateTimeOffset.Now, 0, 1);

        var side = SyncAdvisor.DescribeLocal(world, Status(CloudSyncState.LocalNewerUploadSafe, 3, 3, changed: true));

        Assert.Equal("v3 + new progress", side.Version);
        Assert.Contains("not uploaded", side.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Badge_NotConnected_HasNone()
    {
        Assert.Null(SyncAdvisor.Badge(CloudSyncState.NotConnected, hasLocalChanges: false));
        Assert.Equal(SyncTone.Danger, SyncAdvisor.Badge(CloudSyncState.Conflict, hasLocalChanges: true)!.Tone);
    }
}
