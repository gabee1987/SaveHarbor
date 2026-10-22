using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

// SaveHarbor Cloud sign-in, one question per screen: e-mail, the code from the e-mail, the name friends see, then a
// group. Leaving at any step keeps a valid state: signed out, or signed in without a group.
public sealed partial class CloudOnboardingViewModel : ObservableObject
{
    public const int ResendDelaySeconds = 30;

    private readonly ICloudGroupService _groups;
    private readonly IAppErrorHandler _errorHandler;
    private readonly Action<string> _saveDisplayName;
    private readonly TimeProvider _time;
    private readonly GameId _game;
    private DateTimeOffset? _codeSentAtUtc;

    public CloudOnboardingViewModel(
        ICloudGroupService groups,
        IPlayerIdentity playerIdentity,
        IAppErrorHandler errorHandler,
        GameId game,
        bool otherGameHasGroup,
        Action<string> saveDisplayName,
        TimeProvider? time = null)
    {
        _groups = groups;
        _errorHandler = errorHandler;
        _saveDisplayName = saveDisplayName;
        _time = time ?? TimeProvider.System;
        _game = game;
        displayName = playerIdentity.DisplayName;
        applyToAllGames = !otherGameHasGroup;
        step = groups.IsSignedIn ? CloudOnboardingStep.Group : CloudOnboardingStep.Email;
        Groups = new ObservableCollection<CloudGroupOption>(groups.KnownGroups.Select(group => new CloudGroupOption(group)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailStep), nameof(IsCodeStep), nameof(IsNameStep), nameof(IsGroupStep), nameof(IsCreatedStep))]
    [NotifyPropertyChangedFor(nameof(StepNumber), nameof(SignedInEmail))]
    private CloudOnboardingStep step;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChooseMode), nameof(IsJoinMode), nameof(IsCreateMode))]
    private GroupSetupMode groupMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand))]
    private string email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private string inviteCode = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string groupName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayInitial))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmNameCommand), nameof(JoinCommand), nameof(CreateCommand))]
    private string displayName;

    [ObservableProperty]
    private bool applyToAllGames;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private bool messageIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand), nameof(VerifyCommand), nameof(ResendCommand), nameof(UseDifferentEmailCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmNameCommand), nameof(UseGroupCommand), nameof(JoinCommand), nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseGroupModeCommand), nameof(FinishCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText))]
    [NotifyCanExecuteChangedFor(nameof(ResendCommand))]
    private int resendAvailableIn;

    [ObservableProperty]
    private string createdInviteCode = string.Empty;

    [ObservableProperty]
    private string createdGroupName = string.Empty;

    public ObservableCollection<CloudGroupOption> Groups { get; }

    public bool IsEmailStep => Step == CloudOnboardingStep.Email;

    public bool IsCodeStep => Step == CloudOnboardingStep.Code;

    public bool IsNameStep => Step == CloudOnboardingStep.Name;

    public bool IsGroupStep => Step == CloudOnboardingStep.Group;

    public bool IsCreatedStep => Step == CloudOnboardingStep.Created;

    public bool IsChooseMode => GroupMode == GroupSetupMode.Choose;

    public bool IsJoinMode => GroupMode == GroupSetupMode.Join;

    public bool IsCreateMode => GroupMode == GroupSetupMode.Create;

    // 1 to 4 for the step dots; the screen after creating a group counts as the last step.
    public int StepNumber => Math.Min((int)Step + 1, 4);

    public bool HasGroups => Groups.Count > 0;

    public string? SignedInEmail => _groups.SignedInEmail;

    public string DisplayInitial => SharingText.Initial(DisplayName);

    public string ResendText => ResendAvailableIn > 0 ? $"Send a new code in {ResendAvailableIn} s" : "Send a new code";

    // Set once a group is in use, also while the new group's invite code is still shown.
    public CloudGroupSetupResult? Result { get; private set; }

    // Raised when the player is done and the group can be shown.
    public event EventHandler? Completed;

    // Refreshes the group list when the window opens already signed in.
    public async Task InitializeAsync()
    {
        if (Step == CloudOnboardingStep.Group)
        {
            await RunAsync(LoadGroupsAsync);
        }
    }

    // Called every second by the window while the code step is shown.
    public void UpdateResendCountdown()
    {
        var remaining = _codeSentAtUtc is { } sentAt
            ? ResendDelaySeconds - (int)Math.Floor((_time.GetUtcNow() - sentAt).TotalSeconds)
            : 0;
        ResendAvailableIn = Math.Clamp(remaining, 0, ResendDelaySeconds);
    }

    private bool IsIdle() => !IsBusy;

    private bool CanSendCode() => !IsBusy && !string.IsNullOrWhiteSpace(Email);

    [RelayCommand(CanExecute = nameof(CanSendCode))]
    private Task SendCodeAsync() => RunAsync(RequestCodeAsync);

    private bool CanVerify() => !IsBusy && Code.Count(char.IsAsciiDigit) == 6;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task VerifyAsync() => RunAsync(async () =>
    {
        var result = await _groups.VerifySignInCodeAsync(Email, Code);
        if (!result.IsSuccess)
        {
            Code = string.Empty;
            ShowError(result.Message);
            return;
        }

        ShowInfo(string.Empty);
        await LoadGroupsAsync();

        // Returning players pick one of their groups straight away; new players say who they are first.
        Step = HasGroups ? CloudOnboardingStep.Group : CloudOnboardingStep.Name;
    });

    private bool CanResend() => !IsBusy && ResendAvailableIn == 0;

    [RelayCommand(CanExecute = nameof(CanResend))]
    private Task ResendAsync() => RunAsync(RequestCodeAsync);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void UseDifferentEmail()
    {
        Code = string.Empty;
        _codeSentAtUtc = null;
        UpdateResendCountdown();
        Step = CloudOnboardingStep.Email;
        ShowInfo(string.Empty);
    }

    private bool CanConfirmName() => !IsBusy && !string.IsNullOrWhiteSpace(DisplayName);

    [RelayCommand(CanExecute = nameof(CanConfirmName))]
    private void ConfirmName()
    {
        _saveDisplayName(DisplayName);
        ShowGroupStep(InviteCode.Length > 0 ? GroupSetupMode.Join : GroupSetupMode.Choose);
    }

    private async Task RequestCodeAsync()
    {
        var result = await _groups.RequestSignInCodeAsync(Email);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        _codeSentAtUtc = _time.GetUtcNow();
        UpdateResendCountdown();
        Code = string.Empty;
        Step = CloudOnboardingStep.Code;
        ShowInfo("It can take a minute. Check spam if it does not arrive.");
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError(_errorHandler.Handle(exception, "SaveHarbor Cloud setup", AppLogKeyword.CloudAuth).UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowInfo(string text)
    {
        Message = text;
        MessageIsError = false;
    }

    private void ShowError(string text)
    {
        Message = text;
        MessageIsError = true;
    }
}
