using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private const int MaxVisibleToasts = 5;
    // Short enough for the spark on the time bar to glide rather than jump.
    private static readonly TimeSpan ToastTick = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ToastExitAnimation = TimeSpan.FromMilliseconds(460);

    public bool HasSeveralToasts => Toasts.Count > 1;

    private static TimeSpan ToastDisplayTime(ToastKind kind) => kind switch
    {
        ToastKind.Error => TimeSpan.FromSeconds(25),
        ToastKind.Warning => TimeSpan.FromSeconds(16),
        ToastKind.Info => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(8)
    };

    // Raised from any thread; the toast's whole lifetime runs on the UI thread.
    private void OnToastRequested(object? sender, ToastNotification toast)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ShowToastAsync(toast));
    }

    private async Task ShowToastAsync(ToastNotification toast)
    {
        Toasts.Add(toast);
        while (Toasts.Count > MaxVisibleToasts)
        {
            Toasts.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasSeveralToasts));

        var displayTime = ToastDisplayTime(toast.Kind);
        var shown = TimeSpan.Zero;
        while (shown < displayTime && !toast.IsClosing && Toasts.Contains(toast))
        {
            await Task.Delay(ToastTick);
            if (!toast.IsHovered)
            {
                shown += ToastTick;
                toast.RemainingFraction = Math.Max(0, 1 - (shown / displayTime));
            }
        }

        await CloseToastAsync(toast);
    }

    private async Task CloseToastAsync(ToastNotification toast)
    {
        if (toast.IsClosing || !Toasts.Contains(toast))
        {
            return;
        }

        toast.IsClosing = true;
        await Task.Delay(ToastExitAnimation);
        Toasts.Remove(toast);
        OnPropertyChanged(nameof(HasSeveralToasts));
    }

    [RelayCommand]
    private Task DismissToastAsync(ToastNotification? toast) => toast is null ? Task.CompletedTask : CloseToastAsync(toast);

    [RelayCommand]
    private Task DismissAllToastsAsync() => Task.WhenAll(Toasts.ToArray().Select(CloseToastAsync));
}
