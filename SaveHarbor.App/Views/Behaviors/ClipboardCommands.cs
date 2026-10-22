using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Views.Behaviors;

// "Copy" buttons next to paths: Command="{x:Static behaviors:ClipboardCommands.Copy}" CommandParameter="{Binding Path}".
public static class ClipboardCommands
{
    private const int MaxPreviewLength = 90;

    public static IRelayCommand<string> Copy { get; } = new RelayCommand<string>(CopyText, text => !string.IsNullOrWhiteSpace(text));

    private static void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            ToastService.Current?.Success("Copied", text.Length <= MaxPreviewLength ? text : $"{text[..MaxPreviewLength]}...");
        }
        catch (COMException)
        {
            // Another program is holding the clipboard open.
            ToastService.Current?.Warning("Not copied", "The clipboard is busy. Try again in a moment.");
        }
    }
}
