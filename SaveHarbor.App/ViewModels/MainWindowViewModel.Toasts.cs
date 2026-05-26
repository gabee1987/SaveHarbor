using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private async void OnToastRequested(object? sender, ToastNotification toast)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Toasts.Insert(0, toast);
            while (Toasts.Count > 4)
            {
                Toasts.RemoveAt(Toasts.Count - 1);
            }
        });

        await Task.Delay(TimeSpan.FromSeconds(toast.Kind == ToastKind.Error ? 6 : 3.5));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            toast.IsClosing = true;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(460));

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Toasts.Remove(toast);
        });
    }
}
