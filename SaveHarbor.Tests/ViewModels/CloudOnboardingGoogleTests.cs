using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.ViewModels;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.ViewModels;

// Google sign-in on the e-mail step: offered only when the cloud has it on, continues like a code sign-in, and can be
// cancelled while the browser has the player.
public sealed class CloudOnboardingGoogleTests
{
    private readonly FakeCloudGroupService groups = new();

    private CloudOnboardingViewModel Create() =>
        new(groups, new FixedPlayerIdentity(), new AppErrorHandler(new NullAppLogger()), GameId.Windrose, false, _ => { });

    [Fact]
    public async Task Google_IsOfferedOnlyWhenTheCloudHasItOn()
    {
        var off = Create();
        await off.InitializeAsync();
        groups.GoogleAvailable = true;
        var on = Create();
        await on.InitializeAsync();

        Assert.False(off.CanUseGoogle);
        Assert.False(off.SignInWithGoogleCommand.CanExecute(null));
        Assert.True(on.CanUseGoogle);
        Assert.True(on.SignInWithGoogleCommand.CanExecute(null));
    }

    [Fact]
    public async Task Google_NewPlayer_GoesOnToTheName()
    {
        groups.GoogleAvailable = true;
        groups.GoogleResult = new CloudAuthResult(true, "Signed in as test@example.invalid.");
        var setup = Create();
        await setup.InitializeAsync();

        await setup.SignInWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal(CloudOnboardingStep.Name, setup.Step);
        Assert.False(setup.IsWaitingForBrowser);
        Assert.False(setup.IsBusy);
        Assert.Equal("test@example.invalid", setup.SignedInEmail);
    }

    [Fact]
    public async Task Google_ReturningPlayer_GoesStraightToTheGroups()
    {
        groups.GoogleAvailable = true;
        groups.GoogleResult = new CloudAuthResult(true, "Signed in as test@example.invalid.");
        groups.ServerGroups.Add(new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "member", 2, DateTimeOffset.UnixEpoch));
        var setup = Create();
        await setup.InitializeAsync();

        await setup.SignInWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal(CloudOnboardingStep.Group, setup.Step);
    }

    [Fact]
    public async Task Google_Failure_StaysOnTheEmailWithTheReason()
    {
        groups.GoogleAvailable = true;
        groups.GoogleResult = new CloudAuthResult(false, "Google sign-in was cancelled. Try again, or sign in with your e-mail address.");
        var setup = Create();
        await setup.InitializeAsync();

        await setup.SignInWithGoogleCommand.ExecuteAsync(null);

        Assert.Equal(CloudOnboardingStep.Email, setup.Step);
        Assert.True(setup.MessageIsError);
        Assert.StartsWith("Google sign-in was cancelled", setup.Message);
    }

    [Fact]
    public async Task Google_WhileTheBrowserHasThePlayer_CanBeCancelled()
    {
        groups.GoogleAvailable = true;
        var setup = Create();
        await setup.InitializeAsync();

        var signIn = setup.SignInWithGoogleCommand.ExecuteAsync(null);
        Assert.True(setup.IsWaitingForBrowser);
        Assert.True(setup.IsBusy);
        Assert.False(setup.SendCodeCommand.CanExecute(null));
        Assert.True(setup.CancelBrowserSignInCommand.CanExecute(null));

        setup.CancelBrowserSignInCommand.Execute(null);
        await signIn;

        Assert.Equal(CloudOnboardingStep.Email, setup.Step);
        Assert.False(setup.IsWaitingForBrowser);
        Assert.False(setup.IsBusy);
        Assert.Equal(string.Empty, setup.Message);
        Assert.False(groups.IsSignedIn);
    }
}
