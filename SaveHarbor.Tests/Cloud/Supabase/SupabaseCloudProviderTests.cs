using System.Net;
using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// SaveHarbor Cloud over scripted HTTP: what is sent, in which order, and what the app makes of the answers.
public sealed class SupabaseCloudProviderTests : IDisposable
{
    private const string UploadPath = "storage/v1/object/saves/" + SupabaseHarness.GroupId + "/windrose/TEST_WORLD_ID/" + SupabaseHarness.VersionId + ".zip";

    private readonly SupabaseHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private RecordingHttpMessageHandler Handler => harness.Handler;

    private CloudUploadRequest UploadRequest(long size = 3, CloudWorldManifest? previous = null)
    {
        var archive = harness.Temp.Combine("upload.zip");
        File.WriteAllBytes(archive, [1, 2, 3]);
        return new CloudUploadRequest(
            SupabaseHarness.World,
            archive,
            new CloudVersionMetadata
            {
                VersionNumber = (previous?.LatestVersion?.VersionNumber ?? 0) + 1,
                VersionId = SupabaseHarness.VersionId,
                ArchiveFileName = SupabaseHarness.VersionId + ".zip",
                ArchiveSha256 = new string('A', 64),
                ArchiveSizeBytes = size,
                UploaderMachine = "TEST_DEVICE"
            },
            previous);
    }

    [Fact]
    public async Task Upload_StoresTheArchiveThenPublishesIt()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Post, UploadPath, HttpStatusCode.OK, """{"Key":"saves/x"}""");
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/publish_world_version", "publish_ok.json");

        var result = await harness.Provider.UploadVersionAsync(UploadRequest(), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Manifest!.LatestVersion!.VersionNumber);
        Assert.Equal(["storage/v1/object/saves/" + SupabaseHarness.ObjectPath, "rest/v1/rpc/publish_world_version"],
            Handler.Requests.Select(request => request.Path));

        var upload = Handler.Requests[0];
        Assert.Equal("false", upload.Headers["x-upsert"]);
        Assert.Equal(SupabaseHarness.AccessToken, upload.Bearer);
        Assert.Equal("sb_publishable_TEST", upload.Headers["apikey"]);

        var publish = Handler.Requests[1].Body;
        Assert.Contains("\"p_expected_latest\":0", publish);
        Assert.Contains($"\"archive_path\":\"{SupabaseHarness.ObjectPath}\"", publish);
        Assert.Contains("\"archive_sha256\":\"" + new string('a', 64) + "\"", publish);
    }

    [Fact]
    public async Task Upload_ExpectsThePreviousLatestVersion()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Post, UploadPath, HttpStatusCode.OK);
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/publish_world_version", "publish_ok.json");
        var previous = new CloudWorldManifest { WorldId = "TEST_WORLD_ID", LatestVersion = new CloudVersionMetadata { VersionNumber = 7 } };

        await harness.Provider.UploadVersionAsync(UploadRequest(previous: previous), Token);

        Assert.Contains("\"p_expected_latest\":7", Handler.To("rest/v1/rpc/publish_world_version").Single().Body);
    }

    [Fact]
    public async Task Upload_VersionConflict_DeletesTheUploadedArchive()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Post, UploadPath, HttpStatusCode.OK);
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/publish_world_version", "publish_conflict.json", HttpStatusCode.BadRequest);
        Handler.On(HttpMethod.Delete, "storage/v1/object/saves", HttpStatusCode.OK, "[]");

        var result = await harness.Provider.UploadVersionAsync(UploadRequest(), Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Someone shared a newer version while you were uploading. Download it first.", result.Message);
        var delete = Handler.To("storage/v1/object/saves").Single();
        Assert.Equal(HttpMethod.Delete, delete.Method);
        Assert.Contains(SupabaseHarness.ObjectPath, delete.Body);
    }

    [Fact]
    public async Task Upload_LargerThanTheLimit_SendsNothing()
    {
        await harness.SignInWithGroupAsync();

        var result = await harness.Provider.UploadVersionAsync(UploadRequest(size: harness.Options.MaxArchiveBytes + 1), Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("allows up to 45 MB", result.Message);
        Assert.Empty(Handler.Requests);
    }

    [Fact]
    public async Task Upload_WorldIdTheCloudCannotStore_SendsNothing()
    {
        await harness.SignInWithGroupAsync();
        var request = UploadRequest() with { World = SupabaseHarness.World with { WorldId = "Wörld" } };

        var result = await harness.Provider.UploadVersionAsync(request, Token);

        Assert.False(result.IsSuccess);
        Assert.Empty(Handler.Requests);
    }

    [Fact]
    public async Task Download_WritesTheArchiveFromTheWorldsFolder()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Get, "storage/v1/object/authenticated/saves/" + SupabaseHarness.ObjectPath, HttpStatusCode.OK, "TEST_ARCHIVE");
        var target = harness.Temp.Combine("downloads", "archive.zip");
        var version = new CloudVersionMetadata { VersionNumber = 1, ArchiveFileName = SupabaseHarness.VersionId + ".zip" };

        var result = await harness.Provider.DownloadVersionAsync(new CloudDownloadRequest(SupabaseHarness.World, version, target), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("TEST_ARCHIVE", File.ReadAllText(target));
    }

    [Fact]
    public async Task RemoveWorld_DeletesArchivesBeforeTheWorld()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, VersionRows(
            ("66666666-6666-4666-8666-666666666666", 1, SupabaseHarness.VersionId, SupabaseHarness.ObjectPath),
            ("67666666-6666-4666-8666-666666666666", 2, "other", SupabaseHarness.GroupId + "/windrose/OTHER_WORLD/other.zip")));
        Handler.On(HttpMethod.Delete, "storage/v1/object/saves", HttpStatusCode.OK, "[]");
        Handler.On(HttpMethod.Post, "rest/v1/rpc/remove_world", HttpStatusCode.NoContent);

        var result = await harness.Provider.RemoveWorldAsync(GameId.Windrose, "TEST_WORLD_ID", Token);

        Assert.True(result.IsSuccess);
        var order = Handler.Requests.Select(request => request.Path).ToArray();
        Assert.True(Array.IndexOf(order, "storage/v1/object/saves") < Array.IndexOf(order, "rest/v1/rpc/remove_world"));
        var deleted = Handler.To("storage/v1/object/saves").Single().Body;
        Assert.Contains(SupabaseHarness.ObjectPath, deleted);
        Assert.DoesNotContain("OTHER_WORLD", deleted);
        Assert.Contains(SupabaseHarness.WorldUuid, Handler.To("rest/v1/rpc/remove_world").Single().Body);
    }

    [Fact]
    public async Task ListStoredVersions_NamesArchivesLikeTheRetentionRuleExpects()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, VersionRows(
            ("66666666-6666-4666-8666-666666666666", 1, SupabaseHarness.VersionId, SupabaseHarness.ObjectPath)));

        var stored = await harness.Provider.ListStoredVersionsAsync(GameId.Windrose, "TEST_WORLD_ID", Token);

        Assert.Equal(1, Assert.Single(stored).VersionNumber);
    }

    [Fact]
    public async Task TryAcquire_OwnLockIdReturned_IsAcquired()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/acquire_session_lock", "lock_acquired.json");

        var attempt = await harness.Provider.TryAcquireSessionLockAsync(GameId.Windrose, RequestedLock(), Token);

        Assert.True(attempt.Acquired);
        var body = Handler.To("rest/v1/rpc/acquire_session_lock").Single().Body;
        Assert.Contains($"\"p_world\":\"{SupabaseHarness.WorldUuid}\"", body);
        Assert.Contains($"\"p_device_id\":\"{FixedDeviceIdentity.TestDeviceId}\"", body);
        Assert.Contains("\"p_ttl_seconds\":1200", body);
    }

    [Fact]
    public async Task TryAcquire_SomeoneElsesLockReturned_IsNotAcquired()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/acquire_session_lock", "lock_held.json");

        var attempt = await harness.Provider.TryAcquireSessionLockAsync(GameId.Windrose, RequestedLock(), Token);

        Assert.False(attempt.Acquired);
        Assert.Equal("TEST_FRIEND", attempt.Current.PlayerName);
    }

    [Fact]
    public async Task Heartbeat_LockLost_ReturnsNull()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.On(HttpMethod.Post, "rest/v1/rpc/heartbeat_session_lock", HttpStatusCode.BadRequest, """{"code":"P0001","message":"lock_lost"}""");

        var renewed = await harness.Provider.HeartbeatSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", RequestedLock().LockId, TimeSpan.FromMinutes(20), Token);

        Assert.Null(renewed);
    }

    [Fact]
    public async Task Snapshot_IsOneRequestAndCachesWorldIds()
    {
        await harness.SignInWithGroupAsync();
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.On(HttpMethod.Post, "rest/v1/rpc/release_session_lock", HttpStatusCode.OK, "true");

        var snapshot = await harness.Provider.GetSnapshotAsync(GameId.Windrose, Token);
        await harness.Provider.ClearSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", RequestedLock().LockId, Token);

        Assert.Equal(2, snapshot.Manifests.Count);
        Assert.Single(Handler.To("rest/v1/rpc/get_group_snapshot"));
        Assert.Contains("\"p_group\":\"" + SupabaseHarness.GroupId + "\"", Handler.To("rest/v1/rpc/get_group_snapshot").Single().Body);
        Assert.Contains(SupabaseHarness.WorldUuid, Handler.To("rest/v1/rpc/release_session_lock").Single().Body);
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnceAndRetries()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", HttpStatusCode.Unauthorized, """{"code":"PGRST303","message":"JWT expired"}""");
        Handler.OnFixture(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", "snapshot.json");
        Handler.OnFixture(HttpMethod.Post, "auth/v1/token", "session.json");

        var snapshot = await harness.Provider.GetSnapshotAsync(GameId.Windrose, Token);

        Assert.Equal(2, snapshot.Manifests.Count);
        var calls = Handler.To("rest/v1/rpc/get_group_snapshot");
        Assert.Equal([SupabaseHarness.AccessToken, "TEST_ACCESS_TOKEN_2"], calls.Select(call => call.Bearer));
        Assert.Contains("TEST_REFRESH_TOKEN", Handler.To("auth/v1/token").Single().Body);
    }

    [Fact]
    public async Task RateLimited_SurfacesAsRateLimited()
    {
        await harness.SignInWithGroupAsync();
        Handler.On(HttpMethod.Post, "rest/v1/rpc/get_group_snapshot", HttpStatusCode.TooManyRequests, string.Empty,
            response => response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        var exception = await Assert.ThrowsAsync<SupabaseException>(() => harness.Provider.GetSnapshotAsync(GameId.Windrose, Token));

        Assert.Equal(SupabaseErrorCodes.RateLimited, exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task Status_NotSignedIn_AsksForSignIn()
    {
        var status = await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.False(status.IsConnected);
        Assert.True(status.NeedsSetup);
        Assert.Equal(CloudConnectionReason.SignIn, status.Reason);
        Assert.Equal("Sign in to share worlds with your group.", status.Message);
        Assert.Empty(Handler.Requests);
    }

    [Fact]
    public async Task Status_NoGroupForTheGame_AsksForAGroup()
    {
        harness.SignIn();

        var status = await harness.Provider.GetConnectionStatusAsync(GameId.Dragonwilds, Token);

        Assert.True(status.NeedsSetup);
        Assert.Equal(CloudConnectionReason.Group, status.Reason);
    }

    [Fact]
    public async Task Status_SignedInWithGroup_IsConnected()
    {
        await harness.SignInWithGroupAsync();

        var status = await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.True(status.IsConnected);
        Assert.Equal("SaveHarbor Cloud", status.ProviderName);
        Assert.Equal("test@example.invalid", status.AccountEmail);
    }

    [Fact]
    public async Task Status_NotConfigured_SaysSoWithoutRequests()
    {
        using var unconfigured = new SupabaseHarness(url: string.Empty);

        var status = await unconfigured.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.Equal(CloudConnectionReason.NotConfigured, status.Reason);
        Assert.False(status.NeedsSetup);
        Assert.Empty(unconfigured.Handler.Requests);
    }

    [Fact]
    public async Task Status_CloudUnreachable_IsNotConnected()
    {
        using var offline = new SupabaseHarness();
        offline.SignIn();
        await offline.Settings.SetActiveGroupsAsync([GameId.Windrose], SupabaseHarness.GroupId, Token);
        offline.Handler.On(HttpMethod.Get, "rest/v1/app_meta", HttpStatusCode.BadGateway);

        var status = await offline.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.False(status.IsConnected);
        Assert.Equal(CloudConnectionReason.Unreachable, status.Reason);
        Assert.Contains("paused", status.Message);
    }

    [Fact]
    public void Capabilities_DescribeAGroupCloud()
    {
        var capabilities = harness.Provider.Capabilities;

        Assert.True(capabilities.HasGroups && capabilities.HasAtomicLocks);
        Assert.False(capabilities.HasSharedFolders || capabilities.HasRecycleBin);
        Assert.Equal(10, capabilities.DefaultRetentionCount);
        Assert.Equal(47185920, capabilities.MaxArchiveBytes);
    }

    private static string VersionRows(params (string Id, int Number, string VersionId, string ArchivePath)[] rows) =>
        System.Text.Json.JsonSerializer.Serialize(rows.Select(row => new Dictionary<string, object>
        {
            ["id"] = row.Id,
            ["group_id"] = SupabaseHarness.GroupId,
            ["version_number"] = row.Number,
            ["version_id"] = row.VersionId,
            ["archive_path"] = row.ArchivePath
        }));

    private CloudSessionLock RequestedLock() => new()
    {
        LockId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        WorldId = "TEST_WORLD_ID",
        PlayerName = "TEST_USER",
        MachineName = "TEST_DEVICE",
        DeviceId = FixedDeviceIdentity.TestDeviceId,
        StartedAtUtc = harness.Clock.Now,
        ExpiresAtUtc = harness.Clock.Now.AddMinutes(20),
        BasedOnVersionNumber = 1,
        BasedOnVersionId = SupabaseHarness.VersionId
    };
}
