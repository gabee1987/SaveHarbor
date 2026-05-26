using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public sealed class ToastService : IToastService
{
    public static ToastService? Current { get; private set; }

    public event EventHandler<ToastNotification>? ToastRequested;

    public ToastService()
    {
        Current = this;
    }

    public void Show(ToastKind kind, string title, string message)
    {
        ToastRequested?.Invoke(this, new ToastNotification(Guid.NewGuid(), kind, title, message));
    }

    public void Info(string title, string message)
    {
        Show(ToastKind.Info, title, message);
    }

    public void Success(string title, string message)
    {
        Show(ToastKind.Success, title, message);
    }

    public void Warning(string title, string message)
    {
        Show(ToastKind.Warning, title, message);
    }

    public void Error(string title, string message)
    {
        Show(ToastKind.Error, title, message);
    }
}
