using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public sealed partial class SharingViewModel
{
    private bool IsIdle() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => NotifyActionStates();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ChooseProviderAsync(string? kind) => kind is null ? Task.CompletedTask : RunAsync("Choose how to share", async () =>
    {
        await _services.Selector.SwitchAsync(kind);
        await ShowCurrentAsync();
        if (kind == CloudProviderKind.GoogleDrive && !_services.Folders.HasSharedFolderConfigured(_game))
        {
            await _services.SetupSharedFolderAsync();
            ShowDrive();
        }
    });

    [RelayCommand]
    private void DecideLater()
    {
        Postponed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ChangeDriveFolderAsync() => RunAsync("Choose the shared folder", async () =>
    {
        await _services.SetupSharedFolderAsync();
        ShowDrive();
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SwitchProviderAsync(string? kind)
    {
        if (kind is null || kind == _services.Selector.ActiveKind)
        {
            return Task.CompletedTask;
        }

        var label = kind == CloudProviderKind.Supabase ? "SaveHarbor Cloud" : "Google Drive";
        if (!_services.Dialogs.Confirm("Switch how you share", $"Share through {label} from now on? Nothing is deleted, and you can switch back any time."))
        {
            return Task.CompletedTask;
        }

        return ChooseProviderAsync(kind);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RotateInviteCodeAsync()
    {
        if (Group.GroupId is not { } groupId
            || !_services.Dialogs.Confirm("New invite code", "Make a new invite code? The old code stops working; everyone already in the group stays."))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Make a new invite code", async () =>
        {
            Group.InviteCode = await _services.Groups.RotateInviteCodeAsync(groupId);
            _services.Toasts.Success("New invite code", "Send the new code to anyone who still wants to join.");
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CleanUpAsync() => RunAsync("Clean up unused files", async () =>
    {
        var unused = await _services.Sync.FindUnusedFilesAsync(_game);
        if (unused.Count == 0)
        {
            _services.Toasts.Info("Nothing to clean up", "Every file in the cloud belongs to a shared version.");
            return;
        }

        if (!_services.Dialogs.ConfirmDanger(
                "Clean up unused files",
                $"{unused.Count} file(s) ({DisplayFormatter.FormatBytes(unused.TotalBytes)}) belong to no shared version, for example after an interrupted upload. Delete them? Shared versions are not touched.",
                "Delete files"))
        {
            return;
        }

        var result = await _services.Sync.RemoveUnusedFilesAsync(_game, unused);
        _services.Toasts.Success("Cleaned up", result.Message);
        await Group.LoadStorageAsync();
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task LeaveGroupAsync()
    {
        var name = Group.GroupName;
        if (Group.GroupId is not { } groupId
            || !_services.Dialogs.ConfirmDanger(
                "Leave group",
                $"Leave {name}? You no longer see its worlds. Worlds on this PC and their backups are not touched.",
                "Leave group"))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Leave group", async () =>
        {
            await _services.Groups.LeaveGroupAsync(groupId);
            _services.Toasts.Info("Left group", $"You left {name}.");
            await StartOnboardingAsync(GroupSetupMode.Choose);
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SignOutAsync()
    {
        if (!_services.Dialogs.Confirm("Sign out", "Sign out of SaveHarbor Cloud on this PC? Your worlds stay in the cloud and on this PC."))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Sign out", async () =>
        {
            await _services.Groups.SignOutAsync();
            Group.Reset();
            Profile.Refresh();
            await StartOnboardingAsync(null);
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SwitchGroupAsync(CloudGroupOption? option) => option is null ? Task.CompletedTask : RunAsync("Switch group", async () =>
    {
        var forAllGames = Enum.GetValues<GameId>().All(game => _services.Groups.GetActiveGroupId(game) == Group.GroupId);
        await _services.Groups.SetActiveGroupAsync(_game, option.Group.Id, forAllGames);
        ConnectedGroupName = option.Name;
        await ShowGroupAsync();
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task JoinAnotherGroupAsync() => StartOnboardingAsync(GroupSetupMode.Join);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CreateAnotherGroupAsync() => StartOnboardingAsync(GroupSetupMode.Create);

    // Back from the sign-in to the group, when one is still set up (for example after "Join another group").
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task BackToGroupAsync() => _services.Groups.IsSignedIn && _services.Groups.HasGroupConfigured(_game)
        ? ShowGroupAsync()
        : Task.CompletedTask;

    public bool CanGoBackToGroup => Screen == SharingScreen.SignIn && _services.Groups.IsSignedIn && _services.Groups.HasGroupConfigured(_game);

    partial void OnScreenChanged(SharingScreen value) => OnPropertyChanged(nameof(CanGoBackToGroup));

    private async Task RunAsync(string operation, Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            var error = _services.ErrorHandler.Handle(exception, operation, AppLogKeyword.CloudSync);
            _services.Dialogs.ShowError(operation, error.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyActionStates()
    {
        ChooseProviderCommand.NotifyCanExecuteChanged();
        ChangeDriveFolderCommand.NotifyCanExecuteChanged();
        SwitchProviderCommand.NotifyCanExecuteChanged();
        RotateInviteCodeCommand.NotifyCanExecuteChanged();
        CleanUpCommand.NotifyCanExecuteChanged();
        LeaveGroupCommand.NotifyCanExecuteChanged();
        SignOutCommand.NotifyCanExecuteChanged();
        SwitchGroupCommand.NotifyCanExecuteChanged();
        JoinAnotherGroupCommand.NotifyCanExecuteChanged();
        CreateAnotherGroupCommand.NotifyCanExecuteChanged();
        BackToGroupCommand.NotifyCanExecuteChanged();
    }
}
