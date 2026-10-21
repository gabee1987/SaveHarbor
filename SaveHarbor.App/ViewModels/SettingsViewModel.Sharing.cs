using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public sealed record ProviderOption(string Kind, string Label)
{
    public override string ToString() => Label;
}

// What the Sharing section of Settings works with. ChangeGroupAsync opens the group dialog and refreshes the main
// window; CloudChangedAsync only refreshes it.
public sealed record SettingsSharingServices(
    ICloudProviderSelector Selector,
    ICloudGroupService Groups,
    ICloudSyncService Sync,
    IDeviceIdentity Device,
    IAppErrorHandler ErrorHandler,
    IToastService Toasts,
    Func<Task> ChangeGroupAsync,
    Func<Task> CloudChangedAsync);

public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInviteCode))]
    private string inviteCode = string.Empty;

    [ObservableProperty]
    private string storageUsedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeGroupCommand), nameof(SignOutCommand), nameof(ShowInviteCodeCommand), nameof(RotateInviteCodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(LeaveGroupCommand), nameof(CleanUpUnusedFilesCommand))]
    private bool isCloudBusy;

    public IReadOnlyList<ProviderOption> ProviderChoices => _sharing.Selector.AvailableKinds
        .Select(kind => new ProviderOption(kind, kind == CloudProviderKind.Supabase ? "SaveHarbor Cloud" : "Google Drive"))
        .ToArray();

    // Hidden while the developer override fixes the provider, and when this build offers only one.
    public bool IsProviderChoiceVisible => _sharing.Selector.ActiveKind != CloudProviderKind.LocalTest && ProviderChoices.Count > 1;

    public ProviderOption? SelectedProvider
    {
        get => ProviderChoices.FirstOrDefault(option => option.Kind == _sharing.Selector.ActiveKind);
        set
        {
            if (value is null || value.Kind == _sharing.Selector.ActiveKind)
            {
                return;
            }

            var confirmed = _dialogService.Confirm(
                "Switch cloud provider",
                $"Switch to {value.Label}? Your worlds' sync state is kept separately per provider; nothing is deleted.");
            if (confirmed)
            {
                _ = RunCloudAsync("Switch cloud provider", () => _sharing.Selector.SwitchAsync(value.Kind));
                return;
            }

            // The list already shows the refused choice; put it back once the binding has finished.
            if (SynchronizationContext.Current is { } context)
            {
                context.Post(_ => OnPropertyChanged(nameof(SelectedProvider)), null);
            }
            else
            {
                OnPropertyChanged(nameof(SelectedProvider));
            }
        }
    }

    public bool ShowGroupSettings => _sharing.Selector.ActiveKind == CloudProviderKind.Supabase;

    public bool ShowSharedFolderSettings => !ShowGroupSettings;

    public bool IsSignedIn => _sharing.Groups.IsSignedIn;

    public string SignedInText => _sharing.Groups.SignedInEmail is { Length: > 0 } email ? email : "Not signed in";

    public string ActiveGroupName => ActiveGroup?.Name ?? (_sharing.Groups.HasGroupConfigured(_activeGame.Current.Id) ? "Your group" : "None");

    public bool HasActiveGroup => _sharing.Groups.HasGroupConfigured(_activeGame.Current.Id);

    public bool IsGroupOwner => ActiveGroup?.IsOwner == true;

    public bool IsGroupMember => HasActiveGroup && !IsGroupOwner;

    public bool HasInviteCode => InviteCode.Length > 0;

    public string ChangeGroupText => !IsSignedIn ? "Sign in" : HasActiveGroup ? "Change" : "Choose";

    public string DeviceLabel
    {
        get => _settings.Current.DeviceLabel;
        set
        {
            var normalized = PlayerIdentity.Normalize(value);
            if (normalized == _settings.Current.DeviceLabel)
            {
                return;
            }

            _settings.Update(settings => settings.DeviceLabel = normalized);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveDeviceLabel));
        }
    }

    public string EffectiveDeviceLabel => _sharing.Device.DeviceLabel;

    private CloudGroupSummary? ActiveGroup =>
        _sharing.Groups.GetActiveGroupId(_activeGame.Current.Id) is { } groupId
            ? _sharing.Groups.KnownGroups.FirstOrDefault(group => group.Id == groupId)
            : null;

    private void RefreshSharing()
    {
        NotifySharing();
        InviteCode = string.Empty;
        StorageUsedText = string.Empty;
        if (ShowGroupSettings && HasActiveGroup)
        {
            _ = LoadStorageUsedAsync();
        }
    }

    private void NotifySharing()
    {
        OnPropertyChanged(nameof(ProviderChoices));
        OnPropertyChanged(nameof(IsProviderChoiceVisible));
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(ShowGroupSettings));
        OnPropertyChanged(nameof(ShowSharedFolderSettings));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(SignedInText));
        OnPropertyChanged(nameof(ActiveGroupName));
        OnPropertyChanged(nameof(HasActiveGroup));
        OnPropertyChanged(nameof(IsGroupOwner));
        OnPropertyChanged(nameof(IsGroupMember));
        OnPropertyChanged(nameof(ChangeGroupText));
        OnPropertyChanged(nameof(EffectiveDeviceLabel));
        ShowInviteCodeCommand.NotifyCanExecuteChanged();
        RotateInviteCodeCommand.NotifyCanExecuteChanged();
        LeaveGroupCommand.NotifyCanExecuteChanged();
        CleanUpUnusedFilesCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadStorageUsedAsync()
    {
        try
        {
            if (await _sharing.Sync.GetStorageUsageAsync(_activeGame.Current.Id) is { } usage)
            {
                StorageUsedText = usage.QuotaBytes is { } quota
                    ? $"{DisplayFormatter.FormatBytes(usage.UsedBytes)} of {DisplayFormatter.FormatBytes(quota)}"
                    : DisplayFormatter.FormatBytes(usage.UsedBytes);
            }
        }
        catch (Exception exception)
        {
            StorageUsedText = _sharing.ErrorHandler.Handle(exception, "Read cloud storage use", AppLogKeyword.CloudSync).UserMessage;
        }
    }

    private bool IsCloudIdle() => !IsCloudBusy;

    private bool CanManageGroup() => !IsCloudBusy && HasActiveGroup;

    private bool CanManageAsOwner() => !IsCloudBusy && IsGroupOwner;

    [RelayCommand(CanExecute = nameof(IsCloudIdle))]
    private Task ChangeGroupAsync() => RunCloudAsync("Change group", _sharing.ChangeGroupAsync);

    [RelayCommand(CanExecute = nameof(IsCloudIdle))]
    private Task SignOutAsync()
    {
        if (!_dialogService.Confirm("Sign out", "Sign out of SaveHarbor Cloud on this PC? Your worlds stay in the cloud and on this PC."))
        {
            return Task.CompletedTask;
        }

        return RunCloudAsync("Sign out", async () =>
        {
            await _sharing.Groups.SignOutAsync();
            await _sharing.CloudChangedAsync();
            _sharing.Toasts.Info("Signed out", "SaveHarbor Cloud is signed out on this PC.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanManageAsOwner))]
    private Task ShowInviteCodeAsync() => RunCloudAsync("Show invite code", async () =>
    {
        InviteCode = await _sharing.Groups.GetInviteCodeAsync(_sharing.Groups.GetActiveGroupId(_activeGame.Current.Id)!);
    });

    [RelayCommand(CanExecute = nameof(CanManageAsOwner))]
    private Task RotateInviteCodeAsync()
    {
        if (!_dialogService.Confirm("New invite code", "Make a new invite code? The old code stops working; members already in the group stay."))
        {
            return Task.CompletedTask;
        }

        return RunCloudAsync("Rotate invite code", async () =>
        {
            InviteCode = await _sharing.Groups.RotateInviteCodeAsync(_sharing.Groups.GetActiveGroupId(_activeGame.Current.Id)!);
            _sharing.Toasts.Success("New invite code", "Send the new code to anyone who still wants to join.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanManageGroup))]
    private Task LeaveGroupAsync()
    {
        var name = ActiveGroupName;
        if (!_dialogService.ConfirmDanger(
                "Leave group",
                $"Leave {name}? You no longer see its worlds. Worlds on this PC and their backups are not touched.",
                "Leave group"))
        {
            return Task.CompletedTask;
        }

        return RunCloudAsync("Leave group", async () =>
        {
            await _sharing.Groups.LeaveGroupAsync(_sharing.Groups.GetActiveGroupId(_activeGame.Current.Id)!);
            await _sharing.CloudChangedAsync();
            _sharing.Toasts.Info("Left group", $"You left {name}.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanManageGroup))]
    private Task CleanUpUnusedFilesAsync() => RunCloudAsync("Clean up unused files", async () =>
    {
        var game = _activeGame.Current.Id;
        var unused = await _sharing.Sync.FindUnusedFilesAsync(game);
        if (unused.Count == 0)
        {
            _sharing.Toasts.Info("Nothing to clean up", "Every file in the cloud belongs to a shared version.");
            return;
        }

        if (!_dialogService.ConfirmDanger(
                "Clean up unused files",
                $"{unused.Count} file(s) ({DisplayFormatter.FormatBytes(unused.TotalBytes)}) in the cloud belong to no shared version, for example after an interrupted upload. Delete them? Shared versions are not touched.",
                "Delete files"))
        {
            return;
        }

        var result = await _sharing.Sync.RemoveUnusedFilesAsync(game, unused);
        _sharing.Toasts.Success("Cleaned up", result.Message);
        await LoadStorageUsedAsync();
    });

    private async Task RunCloudAsync(string operation, Func<Task> action)
    {
        IsCloudBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            var error = _sharing.ErrorHandler.Handle(exception, operation, AppLogKeyword.CloudSync);
            _dialogService.ShowError(operation, error.UserMessage);
        }
        finally
        {
            IsCloudBusy = false;
            NotifySharing();
        }
    }
}
