using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

// The SaveHarbor Cloud setup dialog: e-mail, then the code from the e-mail, then a group. Closing it at any step
// leaves a valid state: signed out, or signed in without a group.
public sealed partial class CloudGroupSetupViewModel : ObservableObject
{
    public const int ResendDelaySeconds = 30;

    private readonly ICloudGroupService _groups;
    private readonly IAppErrorHandler _errorHandler;
    private readonly TimeProvider _time;
    private readonly GameId _game;
    private DateTimeOffset? _codeSentAtUtc;

    public CloudGroupSetupViewModel(
        ICloudGroupService groups,
        IPlayerIdentity playerIdentity,
        IAppErrorHandler errorHandler,
        GameId game,
        bool otherGameHasGroup,
        TimeProvider? time = null)
    {
        _groups = groups;
        _errorHandler = errorHandler;
        _time = time ?? TimeProvider.System;
        _game = game;
        displayName = playerIdentity.DisplayName;
        applyToAllGames = !otherGameHasGroup;
        step = groups.IsSignedIn ? CloudGroupSetupStep.Group : CloudGroupSetupStep.Email;
        Groups = new ObservableCollection<CloudGroupOption>(groups.KnownGroups.Select(group => new CloudGroupOption(group)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailStep), nameof(IsCodeStep), nameof(IsGroupStep), nameof(IsCreatedStep), nameof(SignedInEmail), nameof(ShowSignedInHeader))]
    private CloudGroupSetupStep step;

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
    [NotifyCanExecuteChangedFor(nameof(JoinCommand), nameof(CreateCommand))]
    private string displayName;

    [ObservableProperty]
    private bool applyToAllGames;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private bool messageIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand), nameof(VerifyCommand), nameof(ResendCommand), nameof(UseDifferentEmailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseGroupCommand), nameof(JoinCommand), nameof(CreateCommand), nameof(SignOutCommand), nameof(FinishCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText))]
    [NotifyCanExecuteChangedFor(nameof(ResendCommand))]
    private int resendAvailableIn;

    [ObservableProperty]
    private string createdInviteCode = string.Empty;

    public ObservableCollection<CloudGroupOption> Groups { get; }

    public bool IsEmailStep => Step == CloudGroupSetupStep.Email;

    public bool IsCodeStep => Step == CloudGroupSetupStep.Code;

    public bool IsGroupStep => Step == CloudGroupSetupStep.Group;

    public bool IsCreatedStep => Step == CloudGroupSetupStep.Created;

    public bool HasGroups => Groups.Count > 0;

    public bool ShowSignedInHeader => Step is CloudGroupSetupStep.Group or CloudGroupSetupStep.Created && _groups.IsSignedIn;

    public string? SignedInEmail => _groups.SignedInEmail;

    public string ResendText => ResendAvailableIn > 0 ? $"Resend code in {ResendAvailableIn} s" : "Resend code";

    // Set when the dialog closes with a group chosen.
    public CloudGroupSetupResult? Result { get; private set; }

    public event EventHandler? CloseRequested;

    // Refreshes the group list when the dialog opens already signed in.
    public async Task InitializeAsync()
    {
        if (Step == CloudGroupSetupStep.Group)
        {
            await RunAsync(LoadGroupsAsync);
        }
    }

    // Called every second by the dialog while the code step is shown.
    public void UpdateResendCountdown()
    {
        var remaining = _codeSentAtUtc is { } sentAt
            ? ResendDelaySeconds - (int)Math.Floor((_time.GetUtcNow() - sentAt).TotalSeconds)
            : 0;
        ResendAvailableIn = Math.Clamp(remaining, 0, ResendDelaySeconds);
    }

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

        Step = CloudGroupSetupStep.Group;
        ShowInfo(result.Message);
        await LoadGroupsAsync();
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
        Step = CloudGroupSetupStep.Email;
        ShowInfo(string.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task UseGroupAsync(CloudGroupOption? option) => option is null
        ? Task.CompletedTask
        : RunAsync(async () =>
        {
            await _groups.SetActiveGroupAsync(_game, option.Group.Id, ApplyToAllGames);
            Close(option.Group);
        });

    private bool CanJoin() => !IsBusy && !string.IsNullOrWhiteSpace(InviteCode) && !string.IsNullOrWhiteSpace(DisplayName);

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private Task JoinAsync() => RunAsync(async () =>
    {
        var group = await _groups.JoinGroupAsync(InviteCode, DisplayName);
        await _groups.SetActiveGroupAsync(_game, group.Id, ApplyToAllGames);
        Close(group);
    });

    private bool CanCreate() => !IsBusy && !string.IsNullOrWhiteSpace(GroupName) && !string.IsNullOrWhiteSpace(DisplayName);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => RunAsync(async () =>
    {
        var group = await _groups.CreateGroupAsync(GroupName, DisplayName);
        await _groups.SetActiveGroupAsync(_game, group.Id, ApplyToAllGames);

        // The group is in use from now on, also if the dialog is closed while its invite code is shown.
        Result = new CloudGroupSetupResult(group.Id, group.Name);
        CreatedInviteCode = await _groups.GetInviteCodeAsync(group.Id);
        Step = CloudGroupSetupStep.Created;
        ShowInfo($"{group.Name} is ready.");
    });

    // Closes after the invite code of a new group was shown.
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Finish()
    {
        if (Result is not null)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SignOutAsync() => RunAsync(async () =>
    {
        await _groups.SignOutAsync();
        Groups.Clear();
        OnPropertyChanged(nameof(HasGroups));
        Code = string.Empty;
        Step = CloudGroupSetupStep.Email;
        ShowInfo("Signed out on this PC.");
    });

    private bool IsIdle() => !IsBusy;

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
        Step = CloudGroupSetupStep.Code;
        ShowInfo($"{result.Message} Check spam if it takes more than a minute.");
    }

    private async Task LoadGroupsAsync()
    {
        var groups = await _groups.ListGroupsAsync();
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(new CloudGroupOption(group));
        }

        OnPropertyChanged(nameof(HasGroups));
    }

    private void Close(CloudGroupSummary group)
    {
        Result = new CloudGroupSetupResult(group.Id, group.Name);
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
