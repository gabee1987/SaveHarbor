using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.ViewModels;

// The Sharing page opens on what the player needs next and keeps friends, the invite code, storage and the
// player's own name in one place.
public sealed class SharingViewModelTests : IDisposable
{
    private const string OtherGroupId = "12111111-1111-4111-8111-111111111111";

    private readonly TempDirectory temp = new();
    private readonly FakeCloudGroupService groups = new();
    private readonly StubCloudSyncService sync = new();
    private readonly StubCloudSetupService folders = new();
    private readonly RecordingDialogService dialogs = new();
    private readonly RecordingToastService toasts = new();
    private readonly AppSettingsStore settings;
    private FixedProviderSelector selector = new(CloudProviderKind.Supabase);
    private int folderDialogs;

    public SharingViewModelTests()
    {
        settings = new AppSettingsStore(new TestPathProvider(temp));
    }

    public void Dispose() => temp.Dispose();

    private SharingViewModel Create() => new(
        new SharingServices(
            selector, groups, sync, folders, new FixedDeviceIdentity(), settings, new PlayerIdentity(settings),
            new AppErrorHandler(new NullAppLogger()), dialogs, toasts,
            () =>
            {
                folderDialogs++;
                return Task.CompletedTask;
            }),
        GameId.Windrose,
        "Windrose",
        new ManualTimeProvider(DateTimeOffset.UnixEpoch));

    private async Task InGroupAsync(string role)
    {
        groups.IsSignedIn = true;
        groups.SignedInEmail = "test@example.invalid";
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", role, 2, DateTimeOffset.UnixEpoch));
        groups.KnownGroups = groups.ServerGroups.ToArray();
        groups.Members.Add(new CloudGroupMember("TEST_OWNER", "owner", role == "owner", DateTimeOffset.UnixEpoch));
        groups.Members.Add(new CloudGroupMember("TEST_FRIEND", "member", role != "owner", DateTimeOffset.UnixEpoch.AddDays(1)));
        await groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, applyToAllGames: true);
    }

    [Fact]
    public async Task NoProviderYet_OpensOnTheChoice_AndSaveHarborCloudStartsTheSignIn()
    {
        selector = new FixedProviderSelector(string.Empty);
        var sharing = Create();

        await sharing.InitializeAsync();
        Assert.True(sharing.IsWelcome);

        await sharing.ChooseProviderCommand.ExecuteAsync(CloudProviderKind.Supabase);

        Assert.True(sharing.IsSignIn);
        Assert.Equal(CloudOnboardingStep.Email, sharing.Onboarding!.Step);
    }

    [Fact]
    public void DecideLater_ClosesAndPostpones()
    {
        selector = new FixedProviderSelector(string.Empty);
        var sharing = Create();
        var closed = false;
        sharing.CloseRequested += (_, _) => closed = true;

        sharing.DecideLaterCommand.Execute(null);

        Assert.True(closed);
        Assert.True(sharing.Postponed);
    }

    [Fact]
    public async Task Owner_SeesFriendsTheInviteCodeAndStorage()
    {
        await InGroupAsync("owner");
        sync.Usage = new CloudStorageUsage(256L * 1024 * 1024, 1024L * 1024 * 1024);
        var sharing = Create();

        await sharing.InitializeAsync();

        Assert.True(sharing.IsGroup);
        Assert.Equal("TEST_GROUP", sharing.Heading);
        Assert.Equal("You own this group · 2 members", sharing.Subheading);
        Assert.Equal("Shares worlds of both games", sharing.Group.ScopeLine);
        Assert.Equal(["TEST_OWNER", "TEST_FRIEND"], sharing.Group.Members.Select(member => member.Name));
        Assert.Equal("Owner · you", sharing.Group.Members[0].Detail);
        Assert.Equal(FakeCloudGroupService.InviteCode, sharing.Group.InviteCode);
        Assert.Equal(0.25, sharing.Group.StorageFraction, 3);
        Assert.True(sharing.Group.HasStorageQuota);
        Assert.EndsWith("of 1 GB", sharing.Group.StorageText);
    }

    [Fact]
    public async Task Member_IsToldWhoToAskForTheCode()
    {
        await InGroupAsync("member");
        var sharing = Create();

        await sharing.InitializeAsync();

        Assert.False(sharing.Group.IsOwner);
        Assert.Equal(string.Empty, sharing.Group.InviteCode);
        Assert.Equal("Ask TEST_OWNER for the invite code to bring more friends.", sharing.Group.MemberInviteHint);
        Assert.Equal("You", sharing.Group.Members[1].Detail);
    }

    [Fact]
    public async Task SigningIn_EndsOnTheGroupAndNamesIt()
    {
        var sharing = Create();
        await sharing.InitializeAsync();
        var onboarding = sharing.Onboarding!;

        onboarding.Email = "test@example.invalid";
        await onboarding.SendCodeCommand.ExecuteAsync(null);
        onboarding.Code = FakeCloudGroupService.RightCode;
        await onboarding.VerifyCommand.ExecuteAsync(null);
        onboarding.DisplayName = "TEST_FRIEND";
        onboarding.ConfirmNameCommand.Execute(null);
        onboarding.InviteCode = FakeCloudGroupService.InviteCode;
        await onboarding.JoinCommand.ExecuteAsync(null);

        Assert.True(sharing.IsGroup);
        Assert.Equal("TEST_GROUP", sharing.ConnectedGroupName);
        Assert.Equal("TEST_FRIEND", settings.Current.PlayerName);
    }

    [Fact]
    public async Task Rename_IsSavedAndSentToEveryGroup()
    {
        await InGroupAsync("member");
        var sharing = Create();
        await sharing.InitializeAsync();
        groups.KnownGroups = [.. groups.KnownGroups, new CloudGroupSummary(OtherGroupId, "TEST_GROUP_2", "owner", 1, DateTimeOffset.UnixEpoch)];

        sharing.Profile.DisplayName = "  TEST_NEW  ";
        await Task.Yield();

        Assert.Equal("TEST_NEW", settings.Current.PlayerName);
        Assert.Equal("TEST_NEW", groups.DisplayNames[SupabaseHarness.GroupId]);
        Assert.Equal("TEST_NEW", groups.DisplayNames[OtherGroupId]);
        Assert.Equal("T", sharing.Profile.Initial);
    }

    [Fact]
    public async Task Leave_AfterConfirming_OffersTheGroupChoiceAgain()
    {
        await InGroupAsync("member");
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.LeaveGroupCommand.ExecuteAsync(null);

        Assert.Contains("leave", groups.Calls);
        Assert.True(sharing.IsSignIn);
        Assert.Equal(CloudOnboardingStep.Group, sharing.Onboarding!.Step);
        Assert.True(sharing.Onboarding.IsChooseMode);
    }

    [Fact]
    public async Task Leave_NotConfirmed_ChangesNothing()
    {
        await InGroupAsync("member");
        dialogs.Answer = false;
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.LeaveGroupCommand.ExecuteAsync(null);

        Assert.DoesNotContain("leave", groups.Calls);
        Assert.True(sharing.IsGroup);
    }

    [Fact]
    public async Task SignOut_GoesBackToTheEmail()
    {
        await InGroupAsync("owner");
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.SignOutCommand.ExecuteAsync(null);

        Assert.False(groups.IsSignedIn);
        Assert.Equal(CloudOnboardingStep.Email, sharing.Onboarding!.Step);
        Assert.False(sharing.CanGoBackToGroup);
    }

    [Fact]
    public async Task JoinAnother_CanGoBackToTheGroup()
    {
        await InGroupAsync("owner");
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.JoinAnotherGroupCommand.ExecuteAsync(null);
        Assert.True(sharing.Onboarding!.IsJoinMode);
        Assert.True(sharing.CanGoBackToGroup);

        await sharing.BackToGroupCommand.ExecuteAsync(null);
        Assert.True(sharing.IsGroup);
    }

    [Fact]
    public async Task SwitchGroup_UsesTheOtherGroupForTheSameGames()
    {
        await InGroupAsync("owner");
        groups.ServerGroups.Add(new CloudGroupSummary(OtherGroupId, "TEST_GROUP_2", "member", 3, DateTimeOffset.UnixEpoch));
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.SwitchGroupCommand.ExecuteAsync(Assert.Single(sharing.Group.OtherGroups));

        Assert.Equal(OtherGroupId, groups.GetActiveGroupId(GameId.Windrose));
        Assert.Equal(OtherGroupId, groups.GetActiveGroupId(GameId.Dragonwilds));
        Assert.Equal("TEST_GROUP_2", sharing.ConnectedGroupName);
    }

    [Fact]
    public async Task CleanUp_DeletesOnlyAfterConfirming()
    {
        await InGroupAsync("owner");
        sync.Unused = new CloudUnusedFiles(["TEST_PATH/v1.zip"], 12345);
        var sharing = Create();
        await sharing.InitializeAsync();

        dialogs.Answer = false;
        await sharing.CleanUpCommand.ExecuteAsync(null);
        Assert.Empty(sync.Calls);

        dialogs.Answer = true;
        await sharing.CleanUpCommand.ExecuteAsync(null);
        Assert.Equal(["remove-unused"], sync.Calls);
    }

    [Fact]
    public async Task GoogleDrive_ShowsTheFolderAndOffersSaveHarborCloud()
    {
        selector = new FixedProviderSelector(CloudProviderKind.GoogleDrive);
        folders.FolderId = "TEST_FOLDER_ID";
        folders.Saved.Add(new SavedCloudFolder { Game = "windrose", FolderId = "TEST_FOLDER_ID", Name = "TEST_FOLDER" });
        var sharing = Create();

        await sharing.InitializeAsync();

        Assert.True(sharing.IsDrive);
        Assert.Equal("TEST_FOLDER", sharing.DriveFolderText);
        Assert.True(sharing.CanSwitchToCloud);
        Assert.False(sharing.CanSwitchToDrive);
    }

    [Fact]
    public async Task ChoosingGoogleDrive_WithoutAFolder_OpensTheFolderDialog()
    {
        selector = new FixedProviderSelector(string.Empty);
        var sharing = Create();
        await sharing.InitializeAsync();

        await sharing.ChooseProviderCommand.ExecuteAsync(CloudProviderKind.GoogleDrive);

        Assert.Equal(1, folderDialogs);
        Assert.True(sharing.IsDrive);
        Assert.Equal("No shared folder chosen yet.", sharing.DriveFolderText);
    }
}
