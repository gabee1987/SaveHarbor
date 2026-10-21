using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.ViewModels;

// The SaveHarbor Cloud sign-in, one step per screen: e-mail, code, the name friends see, then a group.
public sealed class CloudOnboardingViewModelTests
{
    private readonly FakeCloudGroupService groups = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> savedNames = [];

    private CloudOnboardingViewModel Create(bool otherGameHasGroup = false) =>
        new(groups, new FixedPlayerIdentity(), new AppErrorHandler(new NullAppLogger()), GameId.Windrose, otherGameHasGroup, savedNames.Add, clock);

    private async Task<CloudOnboardingViewModel> SignedInAsync()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);
        setup.Code = FakeCloudGroupService.RightCode;
        await setup.VerifyCommand.ExecuteAsync(null);
        return setup;
    }

    private async Task<CloudOnboardingViewModel> AtGroupStepAsync()
    {
        var setup = await SignedInAsync();
        if (setup.IsNameStep)
        {
            setup.ConfirmNameCommand.Execute(null);
        }

        return setup;
    }

    [Fact]
    public void SignedOut_StartsWithTheEmail()
    {
        var setup = Create();

        Assert.Equal(CloudOnboardingStep.Email, setup.Step);
        Assert.Equal(1, setup.StepNumber);
        Assert.Equal("TEST_USER", setup.DisplayName);
        Assert.Equal("T", setup.DisplayInitial);
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

        Assert.Equal(CloudOnboardingStep.Group, setup.Step);
        Assert.True(setup.IsChooseMode);
        Assert.Equal("3 members · member", Assert.Single(setup.Groups).Detail);
    }

    [Fact]
    public async Task SendCode_GoesToTheCodeAndStartsTheResendWait()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";

        await setup.SendCodeCommand.ExecuteAsync(null);

        Assert.Equal(CloudOnboardingStep.Code, setup.Step);
        Assert.Equal(2, setup.StepNumber);
        Assert.Contains("spam", setup.Message);
        Assert.False(setup.MessageIsError);
        Assert.Equal(CloudOnboardingViewModel.ResendDelaySeconds, setup.ResendAvailableIn);
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
        Assert.Equal("Send a new code in 18 s", setup.ResendText);

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

        Assert.Equal(CloudOnboardingStep.Email, setup.Step);
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

        Assert.Equal(CloudOnboardingStep.Code, setup.Step);
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
    public async Task NewPlayer_ConfirmsTheirNameBeforeTheGroup()
    {
        var setup = await SignedInAsync();
        Assert.Equal(CloudOnboardingStep.Name, setup.Step);
        Assert.Equal(3, setup.StepNumber);

        setup.DisplayName = "  ";
        Assert.False(setup.ConfirmNameCommand.CanExecute(null));

        setup.DisplayName = "TEST_FRIEND";
        setup.ConfirmNameCommand.Execute(null);

        Assert.Equal(["TEST_FRIEND"], savedNames);
        Assert.Equal(CloudOnboardingStep.Group, setup.Step);
        Assert.True(setup.IsChooseMode);
        Assert.Equal(4, setup.StepNumber);
    }

    [Fact]
    public async Task ReturningPlayer_GoesStraightToTheirGroups()
    {
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "owner", 2, DateTimeOffset.UnixEpoch));

        var setup = await SignedInAsync();

        Assert.Equal(CloudOnboardingStep.Group, setup.Step);
        Assert.True(setup.HasGroups);
        Assert.Equal("TEST_GROUP", Assert.Single(setup.Groups).Name);
        Assert.Empty(savedNames);
    }

    [Fact]
    public async Task GroupModes_ShowOneWayInAtATime()
    {
        var setup = await AtGroupStepAsync();

        setup.ChooseGroupModeCommand.Execute(GroupSetupMode.Join);
        Assert.True(setup.IsJoinMode);
        Assert.False(setup.IsChooseMode);

        setup.ChooseGroupModeCommand.Execute(GroupSetupMode.Choose);
        Assert.True(setup.IsChooseMode);
    }

    [Fact]
    public async Task Join_NormalisesTheCodeUsesTheGroupAndCompletes()
    {
        var setup = await AtGroupStepAsync();
        var completed = false;
        setup.Completed += (_, _) => completed = true;
        setup.ChooseGroupModeCommand.Execute(GroupSetupMode.Join);
        setup.InviteCode = "test code-22";

        await setup.JoinCommand.ExecuteAsync(null);

        Assert.Equal(FakeCloudGroupService.InviteCode, groups.LastJoinCode);
        Assert.True(completed);
        Assert.Equal(new CloudGroupSetupResult(SupabaseHarness.GroupId, "TEST_GROUP"), setup.Result);
        Assert.Equal(SupabaseHarness.GroupId, groups.GetActiveGroupId(GameId.Windrose));
    }

    [Fact]
    public async Task Join_UnknownCode_SaysSoAndStays()
    {
        var setup = await AtGroupStepAsync();
        setup.ChooseGroupModeCommand.Execute(GroupSetupMode.Join);
        setup.InviteCode = "TESTCODE99";

        await setup.JoinCommand.ExecuteAsync(null);

        Assert.True(setup.IsJoinMode);
        Assert.Equal("No group has this invite code. Check it with the group owner.", setup.Message);
        Assert.Null(setup.Result);
    }

    [Fact]
    public async Task Create_ShowsTheInviteCodeBeforeCompleting()
    {
        var setup = await AtGroupStepAsync();
        var completed = false;
        setup.Completed += (_, _) => completed = true;
        setup.ChooseGroupModeCommand.Execute(GroupSetupMode.Create);
        setup.GroupName = "TEST_GROUP";

        await setup.CreateCommand.ExecuteAsync(null);

        Assert.Equal(CloudOnboardingStep.Created, setup.Step);
        Assert.Equal(4, setup.StepNumber);
        Assert.Equal("TEST_GROUP", setup.CreatedGroupName);
        Assert.Equal(FakeCloudGroupService.InviteCode, setup.CreatedInviteCode);
        Assert.False(completed);

        // Leaving now still counts: the group is in use.
        Assert.NotNull(setup.Result);
        setup.FinishCommand.Execute(null);
        Assert.True(completed);
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
        var setup = await AtGroupStepAsync();
        setup.ApplyToAllGames = false;

        await setup.UseGroupCommand.ExecuteAsync(setup.Groups[0]);

        Assert.True(groups.HasGroupConfigured(GameId.Windrose));
        Assert.False(groups.HasGroupConfigured(GameId.Dragonwilds));
        Assert.Equal("TEST_GROUP", setup.Result!.GroupName);
    }

    [Fact]
    public void ShowGroupStep_OpensTheChosenWayIn()
    {
        groups.IsSignedIn = true;
        var setup = Create();

        setup.ShowGroupStep(GroupSetupMode.Create);

        Assert.Equal(CloudOnboardingStep.Group, setup.Step);
        Assert.True(setup.IsCreateMode);
    }

    [Fact]
    public async Task DifferentEmail_GoesBackWithoutSigningIn()
    {
        var setup = Create();
        setup.Email = "test@example.invalid";
        await setup.SendCodeCommand.ExecuteAsync(null);

        setup.UseDifferentEmailCommand.Execute(null);

        Assert.Equal(CloudOnboardingStep.Email, setup.Step);
        Assert.Equal(0, setup.ResendAvailableIn);
        Assert.DoesNotContain("verify", groups.Calls);
    }
}
