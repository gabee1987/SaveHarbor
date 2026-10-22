using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

// A world played in a session on this PC whose progress the group does not have yet.
public sealed record PendingUpload(GameWorld World, DateTimeOffset SinceUtc);

// The banner for progress that was not shared when the game closed: upload failed, the app was closed while the game
// ran, or the save needed a look first. It stays until the progress is shared or the player dismisses it.
public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingUpload), nameof(PendingUploadText))]
    [NotifyCanExecuteChangedFor(nameof(RetryPendingUploadCommand), nameof(DiscardPendingUploadCommand))]
    private PendingUpload? pendingUpload;

    // Hidden while the game runs: the progress is still being made.
    public bool HasPendingUpload => PendingUpload is not null && !IsGameRunning;

    public string PendingUploadText => PendingUpload is { } pending
        ? $"Your progress in {pending.World.WorldName} from {FormatPendingSince(pending.SinceUtc)} is not shared yet."
        : string.Empty;

    private static string FormatPendingSince(DateTimeOffset sinceUtc)
    {
        var local = sinceUtc.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date ? $"{local:HH:mm}" : $"{local:d MMM, HH:mm}";
    }

    // Read after a scan and after anything that shares a world, not on every cloud refresh, so the banner does not
    // appear between Play and the game's window.
    private async Task RefreshPendingUploadAsync()
    {
        var game = _activeGame.Current.Id;
        PendingUpload? found = null;
        foreach (var world in Worlds.ToArray())
        {
            if (await _cloudSyncService.GetPendingUploadAsync(world) is { } since)
            {
                found = new PendingUpload(world, since);
                break;
            }
        }

        if (game == _activeGame.Current.Id)
        {
            PendingUpload = found;
        }
    }

    private bool CanResolvePendingUpload() => !IsBusy && PendingUpload is not null;

    [RelayCommand(CanExecute = nameof(CanResolvePendingUpload))]
    private async Task RetryPendingUploadAsync()
    {
        if (PendingUpload is not { } pending)
        {
            return;
        }

        UpdateGameStatus();
        if (IsGameRunning)
        {
            _toastService.Warning($"{ActiveGameName} is running", "Close the game before uploading the world.");
            return;
        }

        if (!ConfirmUploadDespiteHealth(pending.World))
        {
            return;
        }

        await RunBusyAsync($"Sharing {pending.World.WorldName}...", async () =>
        {
            var world = await _activeGame.Current.SaveAdapter.ReadWorldAsync(pending.World.SavePath) ?? pending.World;
            var result = await _cloudSyncService.UploadCurrentAsync(world);
            await RefreshBackupStatsAsync();
            await RefreshCloudAsync("retry upload");
            await RefreshPendingUploadAsync();

            StatusText = result.Message;
            if (!result.IsSuccess)
            {
                AddActivity("Warning", result.Message);
                _toastService.Warning("Upload blocked", result.Message);
                return;
            }

            AddActivity("Success", result.Message);
            _toastService.Success("Progress shared", result.Message);
        });
    }

    // Nothing is deleted: the progress stays in the world on this PC and is shared with the next upload.
    [RelayCommand(CanExecute = nameof(CanResolvePendingUpload))]
    private async Task DiscardPendingUploadAsync()
    {
        if (PendingUpload is not { } pending)
        {
            return;
        }

        var confirmed = _dialogService.Confirm(
            "Stop the reminder",
            $"SaveHarbor stops reminding you about {pending.World.WorldName}.\n\nNothing is deleted: your progress stays in the world on this PC, and the next upload shares it. If a friend shares a newer version first, downloading it replaces this progress (it is backed up first).");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _cloudSyncService.ClearPendingUploadAsync(pending.World);
            PendingUpload = null;
            AddActivity("Info", $"Stopped the reminder for {pending.World.WorldName}. The progress stays on this PC.");
        }
        catch (Exception ex)
        {
            var error = _errorHandler.Handle(ex, "Dismiss pending upload", AppLogKeyword.Ui);
            AddActivity("Error", FormatActivityError(error));
        }
    }
}
