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

    [Theory]
    [InlineData(CloudConnectionReason.SignIn, "Sign in to share", SyncAction.Connect)]
    [InlineData(CloudConnectionReason.Group, "Choose a group", SyncAction.Connect)]
    [InlineData(CloudConnectionReason.Unreachable, "Cloud unreachable", SyncAction.Connect)]
    [InlineData(CloudConnectionReason.UpdateRequired, "Update required", SyncAction.None)]
    [InlineData(CloudConnectionReason.NotConfigured, "Cloud not connected", SyncAction.None)]
    public void Advise_NotConnectedWithAReason_ShowsTheProvidersWords(string reason, string headline, SyncAction action)
    {
        var status = Status(CloudSyncState.NotConnected, null, null) with
        {
            Connection = new CloudConnectionStatus(false, "SaveHarbor Cloud", null, "TEST_PROVIDER_MESSAGE", true, reason)
        };

        var advice = SyncAdvisor.Advise(status);

        Assert.Equal(headline, advice.Headline);
        Assert.Equal("TEST_PROVIDER_MESSAGE", advice.Explanation);
        Assert.Equal(action, advice.Action);
    }

    [Fact]
    public void Advise_NotConnectedWithoutAReason_KeepsTheDriveWording()
    {
        var status = Status(CloudSyncState.NotConnected, null, null) with { Connection = CloudConnectionStatus.NotConnected("Google Drive") };

        Assert.StartsWith("Connect Google Drive", SyncAdvisor.Advise(status).Explanation);
    }

    [Fact]
    public void DescribeLocal_PlayedSinceBase_SaysNotUploaded()
    {
        var world = new GameWorld(GameId.Dragonwilds, "TEST_WORLD", "TEST_WORLD", string.Empty, "C:\\TEST_PATH", DateTimeOffset.Now, DateTimeOffset.Now, 0, 1);

        var side = SyncAdvisor.DescribeLocal(world, Status(CloudSyncState.LocalNewerUploadSafe, 3, 3, changed: true));

        Assert.Equal("v3", side.Version);
        Assert.Contains("not uploaded", side.Detail, StringComparison.Ordinal);
        Assert.True(side.HasNews);
    }

    [Fact]
    public void DescribeLocal_AfterOwnUpload_ShowsSizeAndWhenItWasShared()
    {
        var world = new GameWorld(GameId.Dragonwilds, "TEST_WORLD", "TEST_WORLD", string.Empty, "C:\\TEST_PATH", DateTimeOffset.Now, DateTimeOffset.Now, 2048, 1);
        var status = Status(CloudSyncState.UpToDate, 3, 3);
        status.LocalState.LastDownloadedAtUtc = DateTimeOffset.UtcNow.AddDays(-2);
        status.LocalState.LastUploadedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        var side = SyncAdvisor.DescribeLocal(world, status);

        Assert.Equal("2 KB · you uploaded 5 min ago", side.Extra);
    }

    [Fact]
    public void DescribeLocal_NeverShared_ShowsOnlyTheSize()
    {
        var world = new GameWorld(GameId.Dragonwilds, "TEST_WORLD", "TEST_WORLD", string.Empty, "C:\\TEST_PATH", DateTimeOffset.Now, DateTimeOffset.Now, 2048, 1);

        var side = SyncAdvisor.DescribeLocal(world, Status(CloudSyncState.UpToDate, 3, 3));

        Assert.Equal("2 KB", side.Extra);
    }

    [Theory]
    [InlineData(CloudSyncState.UpToDate, 3, 3, false)]
    [InlineData(CloudSyncState.LocalNewerUploadSafe, 3, 3, true)]
    [InlineData(CloudSyncState.CloudNewer, 2, 3, false)]
    [InlineData(CloudSyncState.Conflict, 2, 3, true)]
    [InlineData(CloudSyncState.ConnectedNoCloudSave, null, null, false)]
    public void Advise_SaysWhatHappensNext(CloudSyncState state, int? localBase, int? latest, bool changed)
    {
        Assert.True(SyncAdvisor.Advise(Status(state, localBase, latest, changed)).HasHint);
    }

    [Fact]
    public void Badge_NotConnected_HasNone()
    {
        Assert.Null(SyncAdvisor.Badge(CloudSyncState.NotConnected, hasLocalChanges: false));
        Assert.Equal(SyncTone.Danger, SyncAdvisor.Badge(CloudSyncState.Conflict, hasLocalChanges: true)!.Tone);
    }
}
