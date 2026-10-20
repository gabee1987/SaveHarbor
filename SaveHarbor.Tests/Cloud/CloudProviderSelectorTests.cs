using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud;

public sealed class CloudProviderSelectorTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly TestPathProvider paths;
    private readonly AppSettingsStore settings;
    private readonly CloudProviderOptions options = new();
    private readonly FakeCloudProvider supabase = new();
    private int supabaseCreated;

    public CloudProviderSelectorTests()
    {
        paths = new TestPathProvider(temp);
        settings = new AppSettingsStore(paths);
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CloudProviderSelector CreateSelector() => new(
        options,
        settings,
        new FolderCloudProvider(paths, new FixedPlayerIdentity(), TimeSpan.Zero),
        new GoogleDriveCloudProvider(paths, options, new FixedPlayerIdentity(), new NullAppLogger(), TimeSpan.Zero),
        () =>
        {
            supabaseCreated++;
            return supabase;
        },
        new NullAppLogger());

    [Fact]
    public async Task NewInstall_NothingChosen_NeedsSetup()
    {
        var selector = CreateSelector();

        var status = await selector.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.Equal(string.Empty, selector.ActiveKind);
        Assert.False(status.IsConnected);
        Assert.True(status.NeedsSetup);
        Assert.Equal(CloudProviderCapabilities.None, selector.Capabilities);
    }

    [Fact]
    public void ExistingSharedFolder_KeepsGoogleDrive()
    {
        options.SetSharedFolderId(GameId.Dragonwilds, "TEST_FOLDER_ID_12345");

        var selector = CreateSelector();

        Assert.Equal(CloudProviderKind.GoogleDrive, selector.ActiveKind);
        Assert.Equal("Google Drive", selector.ProviderName);
        Assert.Equal(CloudProviderKind.GoogleDrive, new AppSettingsStore(paths).Current.CloudProvider);
    }

    [Fact]
    public async Task DeveloperOverride_ForcesLocalTest_AndCannotBeSwitched()
    {
        options.DeveloperProviderOverride = CloudProviderKind.LocalTest;
        settings.Update(current => current.CloudProvider = CloudProviderKind.Supabase);

        var selector = CreateSelector();

        Assert.Equal(CloudProviderKind.LocalTest, selector.ActiveKind);
        Assert.Equal("Local test cloud", selector.ProviderName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => selector.SwitchAsync(CloudProviderKind.GoogleDrive, Token));
    }

    [Fact]
    public async Task Switch_PersistsAndRaisesActiveChangedOncePerChange()
    {
        var selector = CreateSelector();
        var changes = 0;
        selector.ActiveChanged += (_, _) => changes++;

        await selector.SwitchAsync("googledrive", Token);
        await selector.SwitchAsync(CloudProviderKind.GoogleDrive, Token);

        Assert.Equal(1, changes);
        Assert.Equal(CloudProviderKind.GoogleDrive, selector.ActiveKind);
        Assert.Equal(CloudProviderKind.GoogleDrive, new AppSettingsStore(paths).Current.CloudProvider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LocalTest")]
    [InlineData("TEST_UNKNOWN")]
    public async Task Switch_ToAProviderUsersCannotChoose_Throws(string kind)
    {
        var selector = CreateSelector();

        await Assert.ThrowsAsync<ArgumentException>(() => selector.SwitchAsync(kind, Token));
    }

    [Fact]
    public void UnknownSavedChoice_CountsAsNothingChosen()
    {
        settings.Update(current => current.CloudProvider = "TEST_UNKNOWN");

        Assert.Equal(string.Empty, CreateSelector().ActiveKind);
    }

    [Fact]
    public async Task Supabase_IsCreatedOnFirstUseAndReceivesEveryCall()
    {
        var selector = CreateSelector();
        Assert.Equal(0, supabaseCreated);
        await selector.SwitchAsync(CloudProviderKind.Supabase, Token);
        var requested = new CloudSessionLock { LockId = "TEST_LOCK_ID", WorldId = "TEST_WORLD_ID" };

        await selector.GetConnectionStatusAsync(GameId.Windrose, Token);
        await selector.ConnectAsync(GameId.Windrose, Token);
        await selector.GetWorldManifestAsync(GameId.Windrose, "TEST_WORLD_ID", Token);
        await selector.ListWorldManifestsAsync(GameId.Windrose, Token);
        await selector.GetSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", Token);
        await selector.GetSnapshotAsync(GameId.Windrose, Token);
        await selector.TryAcquireSessionLockAsync(GameId.Windrose, requested, Token);
        await selector.HeartbeatSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", "TEST_LOCK_ID", TimeSpan.FromMinutes(20), Token);
        await selector.ClearSessionLockAsync(GameId.Windrose, "TEST_WORLD_ID", "TEST_LOCK_ID", Token);
        await selector.RemoveVersionAsync(GameId.Windrose, "TEST_WORLD_ID", "TEST_ARCHIVE.zip", Token);
        await selector.RemoveWorldAsync(GameId.Windrose, "TEST_WORLD_ID", Token);
        await selector.DisconnectAsync(Token);

        Assert.Equal(1, supabaseCreated);
        Assert.Equal("Fake cloud", selector.ProviderName);
        Assert.Equal(
            [
                "GetConnectionStatus:Windrose", "Connect:Windrose", "GetWorldManifest:Windrose:TEST_WORLD_ID",
                "ListWorldManifests:Windrose", "GetSessionLock:Windrose:TEST_WORLD_ID", "GetSnapshot:Windrose",
                "TryAcquireSessionLock:Windrose:TEST_WORLD_ID", "HeartbeatSessionLock:Windrose:TEST_WORLD_ID",
                "ClearSessionLock:Windrose:TEST_WORLD_ID", "RemoveVersion:TEST_ARCHIVE.zip", "RemoveWorld:Windrose:TEST_WORLD_ID",
                "Disconnect"
            ],
            supabase.Calls);
    }

    [Fact]
    public async Task SharedFolderTest_WhenActiveProviderHasNoFolders_FailsWithoutThrowing()
    {
        var selector = CreateSelector();
        await selector.SwitchAsync(CloudProviderKind.Supabase, Token);

        var result = await selector.TestSharedFolderAsync(GameId.Windrose, "TEST_FOLDER_ID_12345", Token);

        Assert.False(result.IsSuccess);
    }
}
