using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    // The first problem the game's save checks report for the selected world; null when there is none.
    [ObservableProperty]
    private SaveHealthNotice? selectedWorldHealthProblem;

    private void RefreshSaveHealth(GameWorld? world)
    {
        SelectedWorldHealthProblem = world is null ? null : _activeGame.Current.SaveAdapter.CheckHealth(world)?.FirstOrDefault();
    }

    // Uploading a world the game swapped for another copy would hand that copy to the whole group, so it needs consent.
    private bool ConfirmUploadDespiteHealth(GameWorld world)
    {
        var replaced = _activeGame.Current.SaveAdapter.CheckHealth(world)?.FirstOrDefault(notice => notice.Issue == SaveHealthIssue.WorldReplaced);
        return replaced is null || _dialogService.ConfirmDanger(
            "Upload this world?",
            $"{replaced.Title}.\n\n{replaced.Detail}\n\nUploading shares this copy with your group.",
            "Upload anyway");
    }

    private async Task MoveLeftoversAsideAsync(GameWorld world)
    {
        UpdateGameStatus();
        if (IsGameRunning)
        {
            _dialogService.ShowError($"{ActiveGameName} is running", $"Close {ActiveGameName} first; it may have these folders open.");
            return;
        }

        await RunBusyAsync("Moving unfinished copies aside...", () =>
        {
            var moved = _activeGame.Current.SaveAdapter.MoveLeftoversAside(world);
            StatusText = $"Moved {moved.Count} unfinished cop{(moved.Count == 1 ? "y" : "ies")} out of the game's save folder.";
            AddActivity("Success", StatusText);
            _toastService.Success("Unfinished copies moved", moved.Count > 0 ? Path.GetDirectoryName(moved[0])! : StatusText);
            return Task.CompletedTask;
        });
    }
}
