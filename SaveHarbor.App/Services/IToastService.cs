using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IToastService
{
    event EventHandler<ToastNotification>? ToastRequested;

    void Show(ToastKind kind, string title, string message);
    void Info(string title, string message);
    void Success(string title, string message);
    void Warning(string title, string message);
    void Error(string title, string message);
}
