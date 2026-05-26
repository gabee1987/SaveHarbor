using CommunityToolkit.Mvvm.ComponentModel;

namespace SaveHarbor.App.Domain;

public sealed class ToastNotification : ObservableObject
{
    private bool isClosing;

    public ToastNotification(Guid id, ToastKind kind, string title, string message)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Message = message;
    }

    public Guid Id { get; }

    public ToastKind Kind { get; }

    public string Title { get; }

    public string Message { get; }

    public bool IsClosing
    {
        get => isClosing;
        set => SetProperty(ref isClosing, value);
    }
}
