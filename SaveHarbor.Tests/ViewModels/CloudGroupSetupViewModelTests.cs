using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.ViewModels;

// The SaveHarbor Cloud dialog's steps: e-mail, code, group, and the invite code after creating a group.
public sealed class CloudGroupSetupViewModelTests
{
    private readonly FakeCloudGroupService groups = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    private CloudGroupSetupViewModel Create(bool otherGameHasGroup = false) =>
        new(groups, new FixedPlayerIdentity(), new AppErrorHandler(new NullAppLogger()), GameId.Windrose, otherGameHasGroup, clock);

    private async Task<CloudGroupSetupViewModel> SignedInAtGroupStepAsync()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);
        setup.Code = FakeCloudGroupService.RightCode;
        await setup.VerifyCommand.ExecuteAsync(null);
        return setup;
    }

    [Fact]
    public void SignedOut_StartsWithTheEmail()
    {
        var setup = Create();

        Assert.Equal(CloudGroupSetupStep.Email, setup.Step);
        Assert.Equal("TEST_USER", setup.DisplayName);
        Assert.False(setup.SendCodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task SignedIn_StartsWithTheGroupsAndRefreshesThem()
    {
        groups.IsSignedIn = true;
        groups.SignedInEmail = "test@example.invalid";
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "member", 3, DateTimeOffset.UnixEpoch));
        var setup = Create();

        await setup.InitializeAsync();

        Assert.Equal(CloudGroupSetupStep.Group, setup.Step);
        Assert.True(setup.ShowSignedInHeader);
        Assert.Equal("3 member(s) · member", Assert.Single(setup.Groups).Detail);
    }

    [Fact]
    public async Task SendCode_GoesToTheCodeAndStartsTheResendWait()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";

        await setup.SendCodeCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Code, setup.Step);
        Assert.Contains("Check spam", setup.Message);
        Assert.False(setup.MessageIsError);
        Assert.Equal(CloudGroupSetupViewModel.ResendDelaySeconds, setup.ResendAvailableIn);
        Assert.False(setup.ResendCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resend_IsAvailableAfterThirtySeconds()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);

        clock.Advance(TimeSpan.FromSeconds(12));
        setup.UpdateResendCountdown();
        Assert.Equal(18, setup.ResendAvailableIn);
        Assert.Equal("Resend code in 18 s", setup.ResendText);

        clock.Advance(TimeSpan.FromSeconds(18));
        setup.UpdateResendCountdown();
        Assert.True(setup.ResendCommand.CanExecute(null));

        await setup.ResendCommand.ExecuteAsync(null);
        Assert.Equal(2, groups.Calls.Count(call => call == "request"));
    }

    [Fact]
    public async Task InvalidEmail_StaysOnTheEmail()
    {
        var setup = Create();
        setup.Email = "not-an-address";

        await setup.SendCodeCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Email, setup.Step);
        Assert.True(setup.MessageIsError);
    }

    [Fact]
    public async Task WrongCode_KeepsTheCodeStepAndClearsTheField()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);
        setup.Code = "654321";

        await setup.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Code, setup.Step);
        Assert.Equal(string.Empty, setup.Code);
        Assert.Equal("This code is wrong or has expired. Request a new code.", setup.Message);
        Assert.True(setup.MessageIsError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12a45b")]
    public void Verify_NeedsSixDigits(string code)
    {
        var setup = Create();
        setup.Code = code;

        Assert.False(setup.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task RightCode_ListsTheGroups()
    {
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "owner", 2, DateTimeOffset.UnixEpoch));

        var setup = await SignedInAtGroupStepAsync();

        Assert.Equal(CloudGroupSetupStep.Group, setup.Step);
        Assert.True(setup.HasGroups);
        Assert.Equal("TEST_GROUP", Assert.Single(setup.Groups).Name);
        Assert.Equal("test@example.invalid", setup.SignedInEmail);
    }

    [Fact]
    public async Task Join_NormalisesTheCodeUsesTheGroupAndCloses()
    {
        var setup = await SignedInAtGroupStepAsync();
        var closed = false;
        setup.CloseRequested += (_, _) => closed = true;
        setup.InviteCode = "test code-22";

        await setup.JoinCommand.ExecuteAsync(null);

        Assert.Equal(FakeCloudGroupService.InviteCode, groups.LastJoinCode);
        Assert.True(closed);
        Assert.Equal(new CloudGroupSetupResult(SupabaseHarness.GroupId, "TEST_GROUP"), setup.Result);
        Assert.Equal(SupabaseHarness.GroupId, groups.GetActiveGroupId(GameId.Windrose));
    }

    [Fact]
    public async Task Join_UnknownCode_SaysSoAndStays()
    {
        var setup = await SignedInAtGroupStepAsync();
        setup.InviteCode = "TESTCODE99";

        await setup.JoinCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Group, setup.Step);
        Assert.Equal("No group has this invite code. Check it with the group owner.", setup.Message);
        Assert.Null(setup.Result);
    }

    [Fact]
    public async Task Create_ShowsTheInviteCodeBeforeClosing()
    {
        var setup = await SignedInAtGroupStepAsync();
        var closed = false;
        setup.CloseRequested += (_, _) => closed = true;
        setup.GroupName = "TEST_GROUP";

        await setup.CreateCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Created, setup.Step);
        Assert.Equal(FakeCloudGroupService.InviteCode, setup.CreatedInviteCode);
        Assert.False(closed);

        // Closing the dialog now still counts: the group is in use.
        Assert.NotNull(setup.Result);
        setup.FinishCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public void BothGames_IsTheDefaultWhileTheOtherGameHasNoGroup()
    {
        Assert.True(Create(otherGameHasGroup: false).ApplyToAllGames);
        Assert.False(Create(otherGameHasGroup: true).ApplyToAllGames);
    }

    [Fact]
    public async Task UseGroup_ForThisGameOnly_LeavesTheOtherGame()
    {
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "member", 2, DateTimeOffset.UnixEpoch));
        var setup = await SignedInAtGroupStepAsync();
        setup.ApplyToAllGames = false;

        await setup.UseGroupCommand.ExecuteAsync(setup.Groups[0]);

        Assert.True(groups.HasGroupConfigured(GameId.Windrose));
        Assert.False(groups.HasGroupConfigured(GameId.Dragonwilds));
        Assert.Equal("TEST_GROUP", setup.Result!.GroupName);
    }

    [Fact]
    public async Task SignOut_GoesBackToTheEmail()
    {
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "member", 2, DateTimeOffset.UnixEpoch));
        var setup = await SignedInAtGroupStepAsync();

        await setup.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(CloudGroupSetupStep.Email, setup.Step);
        Assert.Empty(setup.Groups);
        Assert.False(groups.IsSignedIn);
    }

    [Fact]
    public async Task DifferentEmail_GoesBackWithoutSigningIn()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);

        setup.UseDifferentEmailCommand.Execute(null);

        Assert.Equal(CloudGroupSetupStep.Email, setup.Step);
        Assert.Equal(0, setup.ResendAvailableIn);
        Assert.DoesNotContain("verify", groups.Calls);
    }
}
