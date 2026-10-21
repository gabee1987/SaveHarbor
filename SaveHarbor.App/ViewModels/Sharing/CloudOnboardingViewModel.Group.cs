using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

// The group step: one of the player's groups, a friend's invite code, or a new group with its code shown before leaving.
public sealed partial class CloudOnboardingViewModel
{
    // Starts again at the group step, for example after leaving a group or to join another one.
    public void ShowGroupStep(GroupSetupMode mode)
    {
        ShowInfo(string.Empty);
        GroupMode = mode;
        Step = CloudOnboardingStep.Group;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ChooseGroupMode(GroupSetupMode mode)
    {
        ShowInfo(string.Empty);
        GroupMode = mode;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task UseGroupAsync(CloudGroupOption? option) => option is null
        ? Task.CompletedTask
        : RunAsync(async () =>
        {
            await _groups.SetActiveGroupAsync(_game, option.Group.Id, ApplyToAllGames);
            Complete(option.Group);
        });

    private bool CanJoin() => !IsBusy && !string.IsNullOrWhiteSpace(InviteCode) && !string.IsNullOrWhiteSpace(DisplayName);

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private Task JoinAsync() => RunAsync(async () =>
    {
        var group = await _groups.JoinGroupAsync(InviteCode, DisplayName);
        await _groups.SetActiveGroupAsync(_game, group.Id, ApplyToAllGames);
        Complete(group);
    });

    private bool CanCreate() => !IsBusy && !string.IsNullOrWhiteSpace(GroupName) && !string.IsNullOrWhiteSpace(DisplayName);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => RunAsync(async () =>
    {
        var group = await _groups.CreateGroupAsync(GroupName, DisplayName);
        await _groups.SetActiveGroupAsync(_game, group.Id, ApplyToAllGames);

        // The group is in use from now on, also if the window is closed while its invite code is shown.
        Result = new CloudGroupSetupResult(group.Id, group.Name);
        CreatedGroupName = group.Name;
        CreatedInviteCode = await _groups.GetInviteCodeAsync(group.Id);
        Step = CloudOnboardingStep.Created;
    });

    // Leaves the invite code of a new group for the group screen.
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Finish()
    {
        if (Result is not null)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
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

    private void Complete(CloudGroupSummary group)
    {
        Result = new CloudGroupSetupResult(group.Id, group.Name);
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
