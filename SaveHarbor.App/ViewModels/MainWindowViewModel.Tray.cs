using CommunityToolkit.Mvvm.ComponentModel;

namespace SaveHarbor.App.ViewModels;

public sealed record TrayBalloon(string Title, string Text);

// While the game runs from Play, the window can wait in the notification area; it comes back when the game closed and
// the progress was shared (or not), or when the game never started.
public partial class MainWindowViewModel
{
    // Steam may take a while to start the game; after this the window comes back on its own.
    private static readonly TimeSpan GameStartTimeout = TimeSpan.FromMinutes(3);

    private DateTimeOffset? trayedAtUtc;

    // Set by the window while it is hidden in the notification area.
    [ObservableProperty]
    private bool isInTray;

    public event EventHandler? MinimizeToTrayRequested;

    public event EventHandler? RestoreFromTrayRequested;

    public event EventHandler<TrayBalloon>? TrayBalloonRequested;

    public string TrayTooltip => SelectedWorld is { } world ? $"SaveHarbor · playing {world.WorldName}" : "SaveHarbor";

    partial void OnIsInTrayChanged(bool value)
    {
        trayedAtUtc = value ? DateTimeOffset.UtcNow : null;
    }

    private void MinimizeToTrayIfEnabled()
    {
        if (_settingsStore.Current.MinimizeToTrayWhilePlaying)
        {
            MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RestoreFromTray()
    {
        if (IsInTray)
        {
            RestoreFromTrayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // Balloons only while the window is hidden; otherwise the toast says it.
    private void NotifyTray(string title, string text)
    {
        if (IsInTray)
        {
            TrayBalloonRequested?.Invoke(this, new TrayBalloon(title, text));
        }
    }

    // The game never showed up after Play (Steam failed or the player cancelled): the window should not stay hidden.
    private void RestoreFromTrayIfGameNeverStarted()
    {
        if (IsInTray && !IsGameRunning && !hasObservedGameRunningDuringSession
            && trayedAtUtc is { } since && DateTimeOffset.UtcNow - since > GameStartTimeout)
        {
            RestoreFromTray();
        }
    }
}
